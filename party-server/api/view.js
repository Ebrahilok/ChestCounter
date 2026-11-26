import { db, HttpError } from '../lib/db.js';

/**
 * Datos de la web de una party: miembros (con sus ajustes) y todas sus partidas. Solo con el enlace secreto de la
 * party (`?t=`), que la app da a sus miembros. No devuelve ids de Epic ni claves.
 */
export default async function handler(req, res) {
  res.setHeader('Cache-Control', 'no-store');
  res.setHeader('X-Robots-Tag', 'noindex');
  try {
    if (req.method !== 'GET') throw new HttpError(405, 'Solo GET');
    const token = typeof req.query.t === 'string' ? req.query.t.trim() : '';
    if (token.length < 16 || token.length > 64) throw new HttpError(404, 'Enlace no válido');
    const sql = await db();
    const party = await sql`SELECT code FROM parties WHERE view_token = ${token}`;
    if (party.length === 0) throw new HttpError(404, 'Esta party ya no existe o el enlace no es válido');
    const code = party[0].code;
    const members = await sql`SELECT id, name, joined_at, last_sync, settings, app_version FROM members
      WHERE party_code = ${code} ORDER BY joined_at`;
    const matches = await sql`SELECT x.member_id, x.session_id, x.start_utc, x.chests, x.kinds, x.playlist, x.placement
      FROM matches x JOIN members m ON m.id = x.member_id WHERE m.party_code = ${code}
      ORDER BY x.start_utc DESC NULLS LAST`;
    res.status(200).json({
      code,
      members: members.map((m) => ({
        id: m.id, name: m.name, joinedAt: m.joined_at, lastSync: m.last_sync,
        settings: m.settings ?? null, appVersion: m.app_version ?? null,
      })),
      matches: matches.map((x) => ({
        member: x.member_id, session: x.session_id, start: x.start_utc, chests: x.chests,
        kinds: x.kinds, playlist: x.playlist, placement: x.placement,
      })),
    });
  } catch (e) {
    const status = e instanceof HttpError ? e.status : 500;
    if (status === 500) console.error(e);
    res.status(status).json({ error: status === 500 ? 'Error del servidor' : e.message });
  }
}
