/**
 * Reading and writing the spreadsheets a storekeeper counts stock in.
 *
 * Without a library. A CSV is text, and an Excel workbook is a zip of XML files the
 * browser can already open: it inflates with `DecompressionStream` and parses with
 * `DOMParser`. That covers the files people actually bring - a sheet of codes and
 * counted quantities - without shipping a spreadsheet engine to every user of the
 * application for the one screen that reads one.
 */

/** The rows of a sheet, each a list of cell texts. */
export type SheetRows = readonly (readonly string[])[];

/** Reads a .csv or .xlsx file into rows of text. */
export async function readSpreadsheet(file: File): Promise<SheetRows> {
  const name = file.name.toLowerCase();

  if (name.endsWith('.csv') || name.endsWith('.txt') || file.type === 'text/csv') {
    return parseCsv(await file.text());
  }

  if (name.endsWith('.xlsx')) {
    return readXlsx(await file.arrayBuffer());
  }

  throw new Error('unsupported');
}

/**
 * Parses CSV text, quoted fields and all.
 *
 * The delimiter is whichever of comma, semicolon and tab the first line holds most of:
 * Excel saves "CSV" with semicolons wherever the decimal separator is a comma.
 */
export function parseCsv(text: string): SheetRows {
  const body = text.startsWith('﻿') ? text.slice(1) : text;
  const firstLine = body.split(/\r?\n/, 1)[0] ?? '';
  const delimiter = [',', ';', '\t'].reduce((best, candidate) =>
    firstLine.split(candidate).length > firstLine.split(best).length ? candidate : best,
  );

  const rows: string[][] = [];
  let row: string[] = [];
  let cell = '';
  let quoted = false;

  for (let index = 0; index < body.length; index++) {
    const char = body[index];

    if (quoted) {
      if (char === '"' && body[index + 1] === '"') {
        cell += '"';
        index++;
      } else if (char === '"') {
        quoted = false;
      } else {
        cell += char;
      }
    } else if (char === '"') {
      quoted = true;
    } else if (char === delimiter) {
      row.push(cell);
      cell = '';
    } else if (char === '\n' || char === '\r') {
      if (char === '\r' && body[index + 1] === '\n') {
        index++;
      }
      row.push(cell);
      rows.push(row);
      row = [];
      cell = '';
    } else {
      cell += char;
    }
  }

  if (cell !== '' || row.length > 0) {
    row.push(cell);
    rows.push(row);
  }

  return rows.filter((candidate) => candidate.some((value) => value.trim() !== ''));
}

/** Writes rows as CSV, quoting whatever needs it. */
export function toCsv(rows: SheetRows): string {
  return rows
    .map((row) =>
      row
        .map((value) =>
          /[",\r\n]/.test(value) ? `"${value.replaceAll('"', '""')}"` : value,
        )
        .join(','),
    )
    .join('\r\n');
}

/** Hands the browser a CSV to save, with a byte order mark so Excel reads it as UTF-8. */
export function downloadCsv(fileName: string, rows: SheetRows): void {
  const blob = new Blob([`﻿${toCsv(rows)}`], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(url);
}

interface ZipEntry {
  readonly method: number;
  readonly compressedSize: number;
  readonly localOffset: number;
}

/** Reads the first worksheet of an .xlsx workbook. */
export async function readXlsx(buffer: ArrayBuffer): Promise<SheetRows> {
  const entries = zipDirectory(buffer);

  const text = async (name: string): Promise<string | null> => {
    const entry = entries.get(name);
    return entry ? new TextDecoder().decode(await inflate(buffer, entry)) : null;
  };

  const sheetPath = (await firstSheetPath(text)) ?? 'xl/worksheets/sheet1.xml';
  const sheet = await text(sheetPath);

  if (!sheet) {
    throw new Error('no-sheet');
  }

  const shared = await text('xl/sharedStrings.xml');
  const strings = shared ? sharedStrings(shared) : [];

  const document = new DOMParser().parseFromString(sheet, 'application/xml');
  const rows: string[][] = [];

  for (const rowElement of Array.from(document.getElementsByTagName('row'))) {
    const cells: string[] = [];

    for (const cell of Array.from(rowElement.getElementsByTagName('c'))) {
      const column = columnIndex(cell.getAttribute('r') ?? '') ?? cells.length;
      const type = cell.getAttribute('t');
      const raw = cell.getElementsByTagName('v')[0]?.textContent ?? '';

      let value: string;

      if (type === 's') {
        value = strings[Number(raw)] ?? '';
      } else if (type === 'inlineStr') {
        value = Array.from(cell.getElementsByTagName('t'))
          .map((piece) => piece.textContent ?? '')
          .join('');
      } else {
        value = raw;
      }

      while (cells.length < column) {
        cells.push('');
      }

      cells[column] = value;
    }

    if (cells.some((value) => value.trim() !== '')) {
      rows.push(cells);
    }
  }

  return rows;
}

/** Where the workbook keeps its first sheet, by way of its relationships. */
async function firstSheetPath(
  text: (name: string) => Promise<string | null>,
): Promise<string | null> {
  const workbook = await text('xl/workbook.xml');
  const relations = await text('xl/_rels/workbook.xml.rels');

  if (!workbook || !relations) {
    return null;
  }

  const parser = new DOMParser();
  const sheet = parser
    .parseFromString(workbook, 'application/xml')
    .getElementsByTagName('sheet')[0];
  const relationId =
    sheet?.getAttribute('r:id') ??
    sheet?.getAttributeNS(
      'http://schemas.openxmlformats.org/officeDocument/2006/relationships',
      'id',
    );

  if (!relationId) {
    return null;
  }

  const target = Array.from(
    parser
      .parseFromString(relations, 'application/xml')
      .getElementsByTagName('Relationship'),
  )
    .find((relation) => relation.getAttribute('Id') === relationId)
    ?.getAttribute('Target');

  if (!target) {
    return null;
  }

  return target.startsWith('/') ? target.slice(1) : `xl/${target}`;
}

function sharedStrings(xml: string): string[] {
  const document = new DOMParser().parseFromString(xml, 'application/xml');

  return Array.from(document.getElementsByTagName('si')).map((item) =>
    Array.from(item.getElementsByTagName('t'))
      .map((piece) => piece.textContent ?? '')
      .join(''),
  );
}

/** A1-style reference to a zero-based column: "C7" is 2. */
function columnIndex(reference: string): number | null {
  const letters = /^[A-Z]+/.exec(reference.toUpperCase())?.[0];

  if (!letters) {
    return null;
  }

  let index = 0;

  for (const letter of letters) {
    index = index * 26 + (letter.charCodeAt(0) - 64);
  }

  return index - 1;
}

/** Reads a zip's central directory: every file's name, method, size and offset. */
function zipDirectory(buffer: ArrayBuffer): Map<string, ZipEntry> {
  const view = new DataView(buffer);
  const entries = new Map<string, ZipEntry>();

  // The end-of-central-directory record sits in the last 64 KiB, after any comment.
  let end = -1;

  for (
    let offset = buffer.byteLength - 22;
    offset >= Math.max(0, buffer.byteLength - 65557);
    offset--
  ) {
    if (view.getUint32(offset, true) === 0x06054b50) {
      end = offset;
      break;
    }
  }

  if (end < 0) {
    throw new Error('not-a-zip');
  }

  const count = view.getUint16(end + 10, true);
  let offset = view.getUint32(end + 16, true);
  const decoder = new TextDecoder();

  for (let index = 0; index < count; index++) {
    if (view.getUint32(offset, true) !== 0x02014b50) {
      throw new Error('not-a-zip');
    }

    const method = view.getUint16(offset + 10, true);
    const compressedSize = view.getUint32(offset + 20, true);
    const nameLength = view.getUint16(offset + 28, true);
    const extraLength = view.getUint16(offset + 30, true);
    const commentLength = view.getUint16(offset + 32, true);
    const localOffset = view.getUint32(offset + 42, true);
    const name = decoder.decode(new Uint8Array(buffer, offset + 46, nameLength));

    entries.set(name, { method, compressedSize, localOffset });
    offset += 46 + nameLength + extraLength + commentLength;
  }

  return entries;
}

/** The bytes of one file in the zip, inflated where they were deflated. */
async function inflate(buffer: ArrayBuffer, entry: ZipEntry): Promise<Uint8Array> {
  const view = new DataView(buffer);
  const nameLength = view.getUint16(entry.localOffset + 26, true);
  const extraLength = view.getUint16(entry.localOffset + 28, true);
  const start = entry.localOffset + 30 + nameLength + extraLength;
  const data = new Uint8Array(buffer.slice(start, start + entry.compressedSize));

  if (entry.method === 0) {
    return data;
  }

  if (entry.method !== 8) {
    throw new Error('unsupported-compression');
  }

  const stream = new Response(data).body?.pipeThrough(
    new DecompressionStream('deflate-raw'),
  );

  return new Uint8Array(await new Response(stream ?? null).arrayBuffer());
}
