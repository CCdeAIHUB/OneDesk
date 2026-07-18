export type NamedEntityKind = "component" | "page" | "scheme" | "plugin";

export interface NamedEntity {
  kind: NamedEntityKind;
  id: string;
  name: string;
}

function normalizeName(name: string) {
  return name.trim().toLocaleLowerCase();
}

export function findNameConflict(items: readonly NamedEntity[], kind: NamedEntityKind, id: string, name: string) {
  const normalized = normalizeName(name);
  if (!normalized) return null;
  return items.find((item) => !(item.kind === kind && item.id === id) && normalizeName(item.name) === normalized) ?? null;
}

export function ensureUniqueName(items: readonly NamedEntity[], baseName: string) {
  const existing = new Set(items.map((item) => normalizeName(item.name)));
  if (!existing.has(normalizeName(baseName))) return baseName;
  let index = 2;
  while (existing.has(normalizeName(`${baseName} ${index}`))) index += 1;
  return `${baseName} ${index}`;
}
