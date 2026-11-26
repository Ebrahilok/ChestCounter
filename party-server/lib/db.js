import { neon } from '@neondatabase/serverless';
import { createHash, randomBytes, randomUUID } from 'node:crypto';

const url = process.env.DATABASE_URL || process.env.POSTGRES_URL;
const sql = url ? neon(url) : null;
let ready;

/** Conexión con las tablas creadas (una vez por instancia). */
export async function db() {
  if (!sql) throw new HttpError(500, 'Falta DATABASE_URL en el servidor');
  ready ??= init().catch((e) => { ready = undefined; throw e; });
  await ready;
  return sql;
}

async function init() {
  await sql`CREATE TABLE IF NOT EXISTS parties (
    code text PRIMARY KEY,
    created_at timestamptz NOT NULL DEFAULT now())`;
  await sql`CREATE TABLE IF NOT EXISTS members (
    id uuid PRIMARY KEY,
    party_code text NOT NULL REFERENCES parties(code) ON DELETE CASCADE,
    epic_id text NOT NULL,
    name text NOT NULL,
    secret_hash text NOT NULL,
    joined_at timestamptz NOT NULL DEFAULT now(),
    last_sync timestamptz,
    UNIQUE (party_code, epic_id))`;
  await sql`CREATE TABLE IF NOT EXISTS matches (
    member_id uuid NOT NULL REFERENCES members(id) ON DELETE CASCADE,
    session_id text NOT NULL,
    start_utc timestamptz,
    chests int NOT NULL,
    kinds jsonb NOT NULL DEFAULT '{}'::jsonb,
    playlist text,
    placement int,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (member_id, session_id))`;
  await sql`CREATE INDEX IF NOT EXISTS matches_updated ON matches (updated_at)`;
}

export class HttpError extends Error {
  constructor(status, message) { super(message); this.status = status; }
}

export const MaxMembers = 16;
const CodeAlphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'; // sin 0/O ni 1/I

export function newCode() {
  return [...randomBytes(6)].map((b) => CodeAlphabet[b % CodeAlphabet.length]).join('');
}

export const newId = () => randomUUID();
export const newSecret = () => randomBytes(32).toString('base64url');
export const hash = (secret) => createHash('sha256').update(String(secret)).digest('hex');

export function text(value, field, max) {
  const v = typeof value === 'string' ? value.trim() : '';
  if (!v) throw new HttpError(400, `Falta ${field}`);
  if (v.length > max) throw new HttpError(400, `${field} demasiado largo`);
  return v;
}

export const code = (value) => text(value, 'code', 12).toUpperCase().replace(/[^A-Z0-9]/g, '');

/** Miembro autenticado por su id y su clave (la clave solo la tiene su app). */
export async function member(sql, body) {
  const id = text(body?.memberId, 'memberId', 64);
  if (!/^[0-9a-f-]{36}$/i.test(id)) throw new HttpError(401, 'Miembro desconocido');
  const rows = await sql`SELECT id, party_code, epic_id, name FROM members
    WHERE id = ${id} AND secret_hash = ${hash(text(body?.secret, 'secret', 200))}`;
  if (rows.length === 0) throw new HttpError(401, 'Ya no estás en esta party');
  return rows[0];
}

/** Envuelve un handler: solo POST JSON; los errores salen como { error }. */
export function route(fn) {
  return async (req, res) => {
    res.setHeader('Cache-Control', 'no-store');
    try {
      if (req.method !== 'POST') throw new HttpError(405, 'Solo POST');
      const body = typeof req.body === 'string' ? JSON.parse(req.body || '{}') : (req.body ?? {});
      res.status(200).json(await fn(body, await db()));
    } catch (e) {
      const status = e instanceof HttpError ? e.status : e instanceof SyntaxError ? 400 : 500;
      if (status === 500) console.error(e);
      res.status(status).json({ error: status === 500 && !(e instanceof HttpError) ? 'Error del servidor' : e.message });
    }
  };
}

/** La party y sus miembros, como la ve cada miembro. */
export async function partyView(sql, partyCode) {
  const members = await sql`SELECT id, epic_id, name, joined_at, last_sync FROM members
    WHERE party_code = ${partyCode} ORDER BY joined_at`;
  return {
    code: partyCode,
    members: members.map((m) => ({
      id: m.id, epicId: m.epic_id, name: m.name, joinedAt: m.joined_at, lastSync: m.last_sync,
    })),
  };
}
