import { route, text, newCode, newId, newSecret, hash, partyView } from '../lib/db.js';

/** Crea una party y su primer miembro. */
export default route(async (body, sql) => {
  const epicId = text(body.epicId, 'epicId', 64);
  const name = text(body.name, 'name', 40);
  for (let i = 0; i < 5; i++) {
    const code = newCode();
    const created = await sql`INSERT INTO parties (code) VALUES (${code}) ON CONFLICT DO NOTHING RETURNING code`;
    if (created.length === 0) continue;
    const id = newId(), secret = newSecret();
    await sql`INSERT INTO members (id, party_code, epic_id, name, secret_hash)
      VALUES (${id}, ${code}, ${epicId}, ${name}, ${hash(secret)})`;
    return { memberId: id, secret, party: await partyView(sql, code) };
  }
  throw new Error('No se pudo generar un código libre');
});
