// SQLite'a kısa yoldan erişim (Durable Object içinde senkron). Birden çok modül ortak kullanır.
export type Satir = Record<string, SqlStorageValue>;

export class Sql {
  constructor(readonly sql: SqlStorage, readonly islem: <T>(f: () => T) => T) {}

  hepsi(q: string, ...p: SqlStorageValue[]): Satir[] { return this.sql.exec(q, ...p).toArray() as Satir[]; }
  bir(q: string, ...p: SqlStorageValue[]): Satir | null { return this.hepsi(q, ...p)[0] ?? null; }
  calistir(q: string, ...p: SqlStorageValue[]): number { return this.sql.exec(q, ...p).rowsWritten; }
  // Tek sütunlu, tek satırlı sayı sorgusu (COUNT gibi).
  sayi(q: string, ...p: SqlStorageValue[]): number { return Number(Object.values(this.bir(q, ...p) ?? { n: 0 })[0] ?? 0); }
}

export const simdiSn = () => Math.floor(Date.now() / 1000);

// "IN (...)" için güvenli sayı listesi (veritabanından gelen tam sayılar; bağlı parametre sınırına takılmaz).
export const liste = (idler: number[]): string => idler.map(x => String(Math.trunc(Number(x)))).join(",") || "NULL";
