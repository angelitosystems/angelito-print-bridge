// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

/**
 * Constructor de tickets ESC/POS (Epson y compatibles). Produce bytes para `bridge.print.raw()`.
 * El texto se codifica en PC850 (español: ñ, tildes, ¿, ¡). Caracteres sin equivalente se reemplazan por `?`.
 *
 * @example
 * const ticket = escpos().init().align("center").bold().text("MI TIENDA").newline()
 *   .bold(false).align("left").line("Café", "S/ 8.50", 42).feed(3).cut().build();
 */
export class EscPosBuilder {
  private readonly chunks: number[] = [];

  /** ESC @ — reinicia la impresora y selecciona PC850. */
  init(): this {
    return this.bytes(0x1b, 0x40, 0x1b, 0x74, 2);
  }

  text(value: string): this {
    for (const ch of value) this.chunks.push(encodePc850(ch));
    return this;
  }

  newline(count = 1): this {
    for (let i = 0; i < count; i++) this.chunks.push(0x0a);
    return this;
  }

  /** Línea con texto a la izquierda y a la derecha, rellenada con espacios hasta `width` columnas. */
  line(left: string, right = "", width = 48): this {
    const space = Math.max(1, width - [...left].length - [...right].length);
    return this.text(left + " ".repeat(space) + right).newline();
  }

  align(value: "left" | "center" | "right"): this {
    return this.bytes(0x1b, 0x61, value === "left" ? 0 : value === "center" ? 1 : 2);
  }

  bold(on = true): this {
    return this.bytes(0x1b, 0x45, on ? 1 : 0);
  }

  underline(on = true): this {
    return this.bytes(0x1b, 0x2d, on ? 1 : 0);
  }

  /** Tamaño de carácter, multiplicadores de 1 a 8. */
  size(width: number, height: number): this {
    const w = clamp(width, 1, 8) - 1;
    const h = clamp(height, 1, 8) - 1;
    return this.bytes(0x1d, 0x21, (w << 4) | h);
  }

  feed(lines = 1): this {
    return this.bytes(0x1b, 0x64, clamp(lines, 0, 255));
  }

  /** GS ( k — código QR nativo de la impresora (modelo 2). Útil para comprobantes electrónicos. */
  qr(data: string, moduleSize = 6, errorLevel: "L" | "M" | "Q" | "H" = "M"): this {
    const payload = new TextEncoder().encode(data);
    if (payload.length > 7089) throw new RangeError("QR demasiado largo");
    const len = payload.length + 3;
    this.bytes(0x1d, 0x28, 0x6b, 4, 0, 0x31, 0x41, 0x32, 0x00); // model 2
    this.bytes(0x1d, 0x28, 0x6b, 3, 0, 0x31, 0x43, clamp(moduleSize, 1, 16));
    this.bytes(0x1d, 0x28, 0x6b, 3, 0, 0x31, 0x45, { L: 48, M: 49, Q: 50, H: 51 }[errorLevel]);
    this.bytes(0x1d, 0x28, 0x6b, len & 0xff, len >> 8, 0x31, 0x50, 0x30, ...payload);
    return this.bytes(0x1d, 0x28, 0x6b, 3, 0, 0x31, 0x51, 0x30);
  }

  /** GS V — avanza y corta (parcial por defecto). Las impresoras sin cortador lo ignoran. */
  cut(mode: "partial" | "full" = "partial"): this {
    return this.bytes(0x1d, 0x56, mode === "full" ? 65 : 66, 0);
  }

  /** ESC p — pulso para abrir el cajón de dinero conectado a la impresora (pin 2 por defecto). */
  openDrawer(pin: 2 | 5 = 2): this {
    return this.bytes(0x1b, 0x70, pin === 2 ? 0 : 1, 25, 250);
  }

  /** Bytes arbitrarios para comandos no cubiertos. */
  bytes(...values: number[]): this {
    for (const v of values) this.chunks.push(v & 0xff);
    return this;
  }

  build(): Uint8Array {
    return Uint8Array.from(this.chunks);
  }
}

export function escpos(): EscPosBuilder {
  return new EscPosBuilder();
}

function clamp(v: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, Math.trunc(v)));
}

const PC850: Record<string, number> = {
  "Ç": 0x80, "ü": 0x81, "é": 0x82, "â": 0x83, "ä": 0x84, "à": 0x85, "ç": 0x87, "ê": 0x88, "ë": 0x89, "è": 0x8a,
  "ï": 0x8b, "î": 0x8c, "ì": 0x8d, "Ä": 0x8e, "É": 0x90, "ô": 0x93, "ö": 0x94, "ò": 0x95, "û": 0x96, "ù": 0x97,
  "Ö": 0x99, "Ü": 0x9a, "£": 0x9c, "á": 0xa0, "í": 0xa1, "ó": 0xa2, "ú": 0xa3, "ñ": 0xa4, "Ñ": 0xa5, "ª": 0xa6,
  "º": 0xa7, "¿": 0xa8, "¡": 0xad, "Á": 0xb5, "Â": 0xb6, "À": 0xb7, "Ê": 0xd2, "Ë": 0xd3, "È": 0xd4, "Í": 0xd6,
  "Î": 0xd7, "Ï": 0xd8, "Ó": 0xe0, "Ô": 0xe2, "Ò": 0xe3, "Ú": 0xe9, "Û": 0xea, "Ù": 0xeb, "°": 0xf8,
};

function encodePc850(ch: string): number {
  const code = ch.codePointAt(0)!;
  if (code >= 0x20 && code < 0x7f) return code;
  if (ch === "\n") return 0x0a;
  return PC850[ch] ?? 0x3f;
}
