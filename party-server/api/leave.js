import { route, member } from '../lib/db.js';

/** Sale de la party: se borran sus partidas subidas. Una party que se queda vacía se borra. */
export default route(async (body, sql) => {
  const me = await member(sql, body);
  await sql`DELETE FROM members WHERE id = ${me.id}`;
  await sql`DELETE FROM parties p WHERE p.code = ${me.party_code}
    AND NOT EXISTS (SELECT 1 FROM members m WHERE m.party_code = p.code)`;
  return { ok: true };
});
