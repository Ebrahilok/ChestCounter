import { route, member, partyView, HttpError, cleanSettings } from '../lib/db.js';

const MaxUpload = 1000;

function clean(m) {
  const sessionId = typeof m?.sessionId === 'string' ? m.sessionId.trim() : '';
  const chests = Number(m?.chests);
  if (!sessionId || sessionId.length > 120 || !Number.isInteger(chests) || chests < 0 || chests > 2000) {
    throw new HttpError(400, 'Partida no válida');
  }
  const kinds = {};
  for (const [k, v] of Object.entries(m.kinds ?? {}).slice(0, 40)) {
    const n = Number(v);
    if (k.length <= 60 && Number.isInteger(n) && n > 0 && n <= 2000) kinds[k] = n;
  }
  const start = m.startUtc ? new Date(m.startUtc) : null;
  return {
    sessionId,
    start: start && !Number.isNaN(start.getTime()) ? start.toISOString() : null,
    chests,
    kinds: JSON.stringify(kinds),
    playlist: typeof m.playlist === 'string' ? m.playlist.slice(0, 120) : null,
    placement: Number.isInteger(m.placement) && m.placement > 0 && m.placement < 1000 ? m.placement : null,
  };
}

/**
 * Sube tus partidas (los cofres exactos de tu propio replay) y devuelve las de toda la party que cambiaron desde
 * `since` (la `serverTime` de la sincronización anterior; sin `since` = todas).
 */
export default route(async (body, sql) => {
  const me = await member(sql, body);
  const upload = Array.isArray(body.matches) ? body.matches : [];
  if (upload.length > MaxUpload) throw new HttpError(413, `Como mucho ${MaxUpload} partidas por envío`);
  const rows = upload.map(clean);

  // Margen de 10 s: una escritura que se confirme justo ahora vuelve a llegar en la siguiente (el cliente deduplica).
  const now = await sql`SELECT now() - interval '10 seconds' AS t`;
  if (rows.length > 0) {
    await sql`INSERT INTO matches (member_id, session_id, start_utc, chests, kinds, playlist, placement)
      SELECT ${me.id}::uuid, u.s, u.t, u.c, u.k::jsonb, u.p, u.pl
      FROM unnest(${rows.map((r) => r.sessionId)}::text[], ${rows.map((r) => r.start)}::timestamptz[],
                  ${rows.map((r) => r.chests)}::int[], ${rows.map((r) => r.kinds)}::text[],
                  ${rows.map((r) => r.playlist)}::text[], ${rows.map((r) => r.placement)}::int[])
           AS u(s, t, c, k, p, pl)
      ON CONFLICT (member_id, session_id) DO UPDATE SET
        start_utc = EXCLUDED.start_utc, chests = EXCLUDED.chests, kinds = EXCLUDED.kinds,
        playlist = EXCLUDED.playlist, placement = EXCLUDED.placement, updated_at = now()
      WHERE (matches.chests, matches.kinds, matches.start_utc, matches.playlist, matches.placement)
        IS DISTINCT FROM (EXCLUDED.chests, EXCLUDED.kinds, EXCLUDED.start_utc, EXCLUDED.playlist, EXCLUDED.placement)`;
  }
  // Ajustes de su app (qué suma a su total) y versión: se ven en la web de la party.
  const settings = cleanSettings(body.settings);
  const appVersion = typeof body.appVersion === 'string' ? body.appVersion.slice(0, 20) : null;
  if (settings || appVersion) {
    await sql`UPDATE members SET last_sync = now(),
      settings = COALESCE(${settings ? JSON.stringify(settings) : null}::jsonb, settings),
      app_version = COALESCE(${appVersion}, app_version) WHERE id = ${me.id}`;
  } else {
    await sql`UPDATE members SET last_sync = now() WHERE id = ${me.id}`;
  }

  const since = body.since ? new Date(body.since) : null;
  const sinceIso = since && !Number.isNaN(since.getTime()) ? since.toISOString() : '1970-01-01T00:00:00Z';
  const changed = await sql`SELECT x.member_id, x.session_id, x.start_utc, x.chests, x.kinds, x.playlist, x.placement
    FROM matches x JOIN members m ON m.id = x.member_id
    WHERE m.party_code = ${me.party_code} AND x.updated_at > ${sinceIso}::timestamptz`;

  return {
    serverTime: now[0].t,
    party: await partyView(sql, me.party_code),
    matches: changed.map((x) => ({
      memberId: x.member_id, sessionId: x.session_id, startUtc: x.start_utc, chests: x.chests,
      kinds: x.kinds, playlist: x.playlist, placement: x.placement,
    })),
  };
});
