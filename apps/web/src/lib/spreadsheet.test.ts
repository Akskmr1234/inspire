import { describe, expect, it } from 'vitest';
import { parseCsv, readXlsx, toCsv } from '@/lib/spreadsheet';

/** Builds a zip the way Excel does, deflating each file unless told to store it. */
async function zip(files: Record<string, string>, deflate = true): Promise<ArrayBuffer> {
  const encoder = new TextEncoder();
  const locals: Uint8Array[] = [];
  const centrals: Uint8Array[] = [];
  let offset = 0;

  for (const [name, content] of Object.entries(files)) {
    const nameBytes = encoder.encode(name);
    const raw = encoder.encode(content);
    const data = deflate
      ? new Uint8Array(
          await new Response(
            new Response(raw).body?.pipeThrough(new CompressionStream('deflate-raw')) ??
              null,
          ).arrayBuffer(),
        )
      : raw;

    const local = new Uint8Array(30 + nameBytes.length + data.length);
    const lv = new DataView(local.buffer);
    lv.setUint32(0, 0x04034b50, true);
    lv.setUint16(8, deflate ? 8 : 0, true);
    lv.setUint32(18, data.length, true);
    lv.setUint32(22, raw.length, true);
    lv.setUint16(26, nameBytes.length, true);
    local.set(nameBytes, 30);
    local.set(data, 30 + nameBytes.length);

    const central = new Uint8Array(46 + nameBytes.length);
    const cv = new DataView(central.buffer);
    cv.setUint32(0, 0x02014b50, true);
    cv.setUint16(10, deflate ? 8 : 0, true);
    cv.setUint32(20, data.length, true);
    cv.setUint32(24, raw.length, true);
    cv.setUint16(28, nameBytes.length, true);
    cv.setUint32(42, offset, true);
    central.set(nameBytes, 46);

    locals.push(local);
    centrals.push(central);
    offset += local.length;
  }

  const directorySize = centrals.reduce((sum, part) => sum + part.length, 0);
  const end = new Uint8Array(22);
  const ev = new DataView(end.buffer);
  ev.setUint32(0, 0x06054b50, true);
  ev.setUint16(8, centrals.length, true);
  ev.setUint16(10, centrals.length, true);
  ev.setUint32(12, directorySize, true);
  ev.setUint32(16, offset, true);

  const parts = [...locals, ...centrals, end];
  const whole = new Uint8Array(parts.reduce((sum, part) => sum + part.length, 0));
  let at = 0;

  for (const part of parts) {
    whole.set(part, at);
    at += part.length;
  }

  return whole.buffer;
}

const SHEET = `<?xml version="1.0" encoding="UTF-8"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
<row r="1"><c r="A1" t="s"><v>0</v></c><c r="C1" t="s"><v>1</v></c></row>
<row r="2"><c r="A2" t="s"><v>2</v></c><c r="C2"><v>12.5</v></c></row>
<row r="3"><c r="A3" t="inlineStr"><is><t>B-2</t></is></c><c r="C3"><v>0</v></c></row>
</sheetData></worksheet>`;

const STRINGS = `<?xml version="1.0" encoding="UTF-8"?>
<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
<si><t>Code</t></si><si><t>Counted</t></si><si><r><t>A-</t></r><r><t>1</t></r></si></sst>`;

describe('parseCsv', () => {
  it('reads quoted fields, doubled quotes and both kinds of line ending', () => {
    expect(parseCsv('Code,Name,Counted\r\n"A,1","Say ""hi""",3\nB,Plain,4\n')).toEqual([
      ['Code', 'Name', 'Counted'],
      ['A,1', 'Say "hi"', '3'],
      ['B', 'Plain', '4'],
    ]);
  });

  it('takes the semicolon Excel uses where the decimal separator is a comma', () => {
    expect(parseCsv('﻿Code;Counted\nA;1,5\n\n')).toEqual([
      ['Code', 'Counted'],
      ['A', '1,5'],
    ]);
  });

  it('round-trips what it writes', () => {
    const rows = [
      ['Code', 'Note'],
      ['A', 'has, a comma'],
      ['B', 'has "quotes"'],
    ];
    expect(parseCsv(toCsv(rows))).toEqual(rows);
  });
});

describe('readXlsx', () => {
  it('reads shared, rich and inline strings and leaves gaps where cells are missing', async () => {
    const workbook = await zip({
      'xl/worksheets/sheet1.xml': SHEET,
      'xl/sharedStrings.xml': STRINGS,
    });

    expect(await readXlsx(workbook)).toEqual([
      ['Code', '', 'Counted'],
      ['A-1', '', '12.5'],
      ['B-2', '', '0'],
    ]);
  });

  it('follows the workbook to its first sheet, stored or deflated', async () => {
    const workbook = await zip(
      {
        'xl/workbook.xml':
          '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Count" sheetId="1" r:id="rId7"/></sheets></workbook>',
        'xl/_rels/workbook.xml.rels':
          '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId7" Target="worksheets/count.xml"/></Relationships>',
        'xl/worksheets/count.xml': SHEET,
        'xl/sharedStrings.xml': STRINGS,
      },
      false,
    );

    expect((await readXlsx(workbook))[1]).toEqual(['A-1', '', '12.5']);
  });
});
