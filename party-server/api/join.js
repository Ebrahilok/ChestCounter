import { route, text, code as parseCode, newId, newSecret, hash, partyView, HttpError, MaxMembers } from '../lib/db.js';

/**
 * Entra en una party con su código. Si esa cuenta de Epic ya estaba (reinstaló la app), conserva sus partidas
 * subidas y recibe una clave nueva: la antigua deja de valer.
 */
export default route(async (body, sql) => {
  const code = parseCode(body.code);
  const epicId = text(body.epicId, 'epicId', 64);
  const name = text(body.name, 'name', 40);
  const party = await sql`SELECT code FROM parties WHERE code = ${code}`;
  if (party.length === 0) throw new HttpError(404, 'No existe ninguna party con ese código');

  const secret = newSecret();
  const again = await sql`UPDATE members SET secret_hash = ${hash(secret)}, name = ${name}
    WHERE party_code = ${code} AND epic_id = ${epicId} RETURNING id`;
  if (again.length > 0) return { memberId: again[0].id, secret, party: await partyView(sql, code) };

  const count = await sql`SELECT count(*)::int AS n FROM members WHERE party_code = ${code}`;
  if (count[0].n >= MaxMembers) throw new HttpError(409, `La party está llena (máximo ${MaxMembers})`);
  const id = newId();
  await sql`INSERT INTO members (id, party_code, epic_id, name, secret_hash)
    VALUES (${id}, ${code}, ${epicId}, ${name}, ${hash(secret)})`;
  return { memberId: id, secret, party: await partyView(sql, code) };
});
