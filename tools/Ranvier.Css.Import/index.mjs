import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash, randomUUID } from 'node:crypto';
import postcss from 'postcss';
import { parse as parseValue, ident } from 'css-tree';

const directory = path.dirname(fileURLToPath(import.meta.url));
const reserved = new Set('abstract and as asr assert atomic base begin break checked class component const constraint constructor continue default delegate do done downcast downto eager elif else end event exception extern external false finally fixed for fun function global if in include inherit inline interface internal land lazy let lor lsl lsr lxor match member method mixin mod module mutable namespace new not null object of open or override parallel private process protected public pure rec return sealed sig static struct tailcall then to trait true try type upcast use val virtual void volatile when while with yield'.split(' '));

function identifier(value) {
  if (typeof value !== 'string' || !/^[A-Za-z_][A-Za-z0-9_']*$/.test(value)) throw new Error(`Invalid F# identifier: ${value}`);
  return reserved.has(value) ? '``' + value + '``' : value;
}

function qualified(value) {
  if (typeof value !== 'string' || !value) throw new Error('Expected an F# module or namespace name.');
  return value.split('.').map(identifier).join('.');
}

function literal(value) {
  return '"' + [...value].map(char => {
    if (char === '\\') return '\\\\';
    if (char === '"') return '\\"';
    if (char.charCodeAt(0) < 32 || char === '\u2028' || char === '\u2029') return '\\u' + char.charCodeAt(0).toString(16).padStart(4, '0');
    return char;
  }).join('') + '"';
}

function decimalIdentity(text) {
  const match = /^([+-]?)(\d*\.?\d+)(?:[eE]([+-]?\d+))?$/.exec(text);
  if (!match) return null;
  const [whole, fraction = ''] = match[2].split('.');
  let digits = (whole + fraction).replace(/^0+/, '');
  if (!digits) return '0';
  let power = BigInt(match[3] ?? '0') - BigInt(fraction.length);
  while (digits.endsWith('0')) { digits = digits.slice(0, -1); power++; }
  return (match[1] === '-' ? '-' : '') + digits + 'e' + power;
}

function numberLiteral(text, integer) {
  const value = Number(text);
  if (!Number.isFinite(value) || decimalIdentity(String(value)) !== decimalIdentity(text)) return null;
  if (integer) return Number.isInteger(value) && value >= -2147483648 && value <= 2147483647 ? String(value) : null;
  const rendered = String(value);
  return /[.eE]/.test(rendered) ? rendered : rendered + '.0';
}

function prepare(manifest) {
  if (manifest?.schemaVersion === 2 && !manifest.sugar) return prepareCompact(manifest);
  const sugar = manifest?.sugar;
  if (!sugar || sugar.schemaVersion !== 2 || !Array.isArray(sugar.properties) || !Array.isArray(sugar.primitives) || !Array.isArray(sugar.keywords)) {
    throw new Error('The binding manifest has no sugar map. Regenerate it with the current specification tool and --runtime.');
  }
  qualified(sugar.namespaceName);
  for (const item of [...sugar.properties, ...sugar.primitives, ...sugar.keywords]) identifier(item.memberName);
  const properties = new Map();
  for (const item of sugar.properties) {
    if (item.scopes.length === 0 && !item.obsolete) properties.set(item.cssName.toLowerCase(), item);
  }
  return { ...sugar, properties, keywords: new Map(sugar.keywords.map(item => [item.token, item])) };
}

function prepareCompact(manifest) {
  qualified(manifest.namespaceName);
  if (typeof manifest.encodedGrammar !== 'string' || !Array.isArray(manifest.properties) || !Array.isArray(manifest.factories)) {
    throw new Error('The compact binding manifest requires encoded grammar, properties and explicit factories.');
  }
  const fields = [];
  const encoded = manifest.encodedGrammar;
  for (let offset = 0; offset < encoded.length;) {
    const colon = encoded.indexOf(':', offset);
    const size = encoded.slice(offset, colon);
    if (colon < 0 || !/^\d+$/.test(size) || !Number.isSafeInteger(Number(size)) || Number(size) > encoded.length - colon - 1) throw new Error('Invalid compact grammar field.');
    fields.push(encoded.slice(colon + 1, colon + 1 + Number(size)));
    offset = colon + 1 + Number(size);
  }
  const integer = text => {
    if (!/^-?\d+$/.test(text) || !Number.isSafeInteger(Number(text))) throw new Error('Invalid compact grammar integer.');
    return Number(text);
  };
  if (!fields.length || integer(fields[0]) < 0 || fields.length !== 1 + integer(fields[0]) * 14) throw new Error('Invalid compact grammar node count.');
  const nodes = new Map();
  const bound = (value, unitName) => {
    if (value === '') return { kind: 'unbounded' };
    if (!Number.isFinite(Number(value))) throw new Error('Invalid compact numeric bound.');
    return { kind: 'finite', value, unitName };
  };
  for (let offset = 1; offset < fields.length; offset += 14) {
    const at = index => fields[offset + index];
    const node = {
      id: integer(at(0)), kind: integer(at(1)), text: at(2), target: integer(at(3)),
      children: at(4) === '' ? [] : at(4).split(',').map(integer),
      minimum: integer(at(6)), maximum: integer(at(7)),
      ranges: [{ minimum: bound(at(9), at(10)), maximum: bound(at(11), at(12)) }]
    };
    if (node.id < 0 || nodes.has(node.id) || node.kind < 0 || node.kind > 12 || node.minimum < 0 || node.maximum < -1 || (node.maximum >= 0 && node.maximum < node.minimum)) throw new Error('Invalid compact grammar node.');
    nodes.set(node.id, node);
  }
  for (const node of nodes.values()) {
    if (node.children.some(id => !nodes.has(id)) || (node.kind === 2 && !nodes.has(node.target))) throw new Error('Missing compact grammar edge.');
    if ([7, 8, 11].includes(node.kind) && node.children.length !== 1) throw new Error('Invalid compact unary grammar.');
  }
  const properties = new Map();
  for (const item of manifest.properties) {
    qualified(item.owner); identifier(item.memberName);
    if (!nodes.has(item.nodeId)) throw new Error('Unknown compact property node.');
    if (!properties.has(item.cssName.toLowerCase())) properties.set(item.cssName.toLowerCase(), item);
  }
  const factories = manifest.factories.filter(factory => ['number', 'integer', 'string', 'token', 'wide'].includes(factory.kind));
  for (const factory of factories) {
    if (!nodes.has(factory.nodeId)) throw new Error('Unknown compact factory node.');
  }
  factories.sort((left, right) => {
    const rank = factory => factory.owner === manifest.namespaceName + '.Css' ? 0 : factory.owner.includes('.Keywords') ? 2 : 1;
    return rank(left) - rank(right) || left.owner.length - right.owner.length || left.owner.localeCompare(right.owner) || left.memberName.localeCompare(right.memberName);
  });
  const owners = new Map((manifest.owners ?? []).map(owner => [owner.nodeId, manifest.namespaceName + '.CssValue.' + owner.path.join('.')]));
  return { compact: true, namespaceName: manifest.namespaceName, properties, factories, nodes, owners };
}

function compactNullable(map, id, seen = new Set()) {
  if (seen.has(id)) return false;
  const node = map.nodes.get(id), next = new Set(seen).add(id);
  const visit = child => compactNullable(map, child, next);
  if (node.kind === 1) return node.text === '';
  if (node.kind === 2) return visit(node.target);
  if (node.kind === 3) return node.children.some(visit);
  if ([4, 5].includes(node.kind)) return node.children.every(visit);
  if (node.kind === 7) return node.minimum === 0 || visit(node.children[0]);
  if (node.kind === 11) return visit(node.children[0]);
  return false;
}

function compactAdmits(map, id, atom, seen = new Set()) {
  if (seen.has(id)) return false;
  const node = map.nodes.get(id), next = new Set(seen).add(id);
  const visit = child => compactAdmits(map, child, atom, next);
  let accepted = atom.nodeId === id;
  if (!accepted) {
    if (node.kind === 1) accepted = atom.nodeId === -1 && atom.text === node.text;
    else if (node.kind === 2) accepted = visit(node.target);
    else if ([3, 6, 8, 11].includes(node.kind)) accepted = node.children.some(visit);
    else if ([4, 5].includes(node.kind)) accepted = node.children.some((child, index) => node.children.every((sibling, other) => index === other || compactNullable(map, sibling)) && visit(child));
    else if (node.kind === 7 && node.minimum <= 1 && (node.maximum === -1 || node.maximum >= 1)) accepted = visit(node.children[0]);
  }
  return accepted && (atom.magnitude === undefined || rangesAllow(atom.magnitude, atom.unit, node.ranges));
}

function compactNumericIdentity(map, factory) {
  const compatible = name => {
    name = name.replace(/^<|>$/g, '').toLowerCase();
    if (factory.unitName === '%') return ['percentage', 'percentage-token'].includes(name);
    if (!factory.unitName) return ['number', 'number-token', 'integer', 'zero'].includes(name);
    return ['length', 'angle', 'time', 'frequency', 'resolution', 'flex', 'dimension', 'dimension-token'].includes(name);
  };
  const terminals = new Set();
  const visit = (id, seen = new Set()) => {
    if (seen.has(id)) return;
    const node = map.nodes.get(id), next = new Set(seen).add(id);
    if (node.kind === 0 && compatible(node.text)) terminals.add(id);
    else if (node.kind === 2) visit(node.target, next);
    else if ([3, 8].includes(node.kind)) node.children.forEach(child => visit(child, next));
  };
  visit(factory.nodeId);
  return terminals.size === 1 ? [...terminals][0] : factory.nodeId;
}

function compactTypedValue(token, property, map) {
  const call = factory => {
    const owner = factory.owner === map.namespaceName + '.Css' ? 'Css' : factory.owner.startsWith(map.namespaceName + '.') ? qualified(factory.owner.slice(map.namespaceName.length + 1)) : qualified(factory.owner);
    return owner + '.' + identifier(factory.memberName);
  };
  if (['Identifier', 'Number'].includes(token.type)) {
    const text = token.type === 'Identifier' ? ident.decode(token.name).toLowerCase() : token.value;
    const preferred = map.owners.get(property.nodeId);
    const factories = map.factories.filter(factory => ['token', 'wide'].includes(factory.kind) && map.nodes.get(factory.nodeId).text === text);
    factories.sort((a, b) => Number(b.owner === preferred) - Number(a.owner === preferred));
    for (const factory of factories) {
      if (factory.kind === 'wide' || compactAdmits(map, property.nodeId, { nodeId: -1, text })) return call(factory);
    }
  }
  for (const factory of map.factories) {
    let argument = null;
    const numeric = ['number', 'integer'].includes(factory.kind);
    if (numeric && token.type === 'Dimension' && factory.unitName && ident.decode(token.unit).toLowerCase() === factory.unitName.toLowerCase()) argument = numberLiteral(token.value, factory.argumentType === 'int');
    else if (numeric && token.type === 'Percentage' && factory.unitName === '%') argument = numberLiteral(token.value, false);
    else if (numeric && token.type === 'Number' && !factory.unitName) argument = numberLiteral(token.value, factory.argumentType === 'int' || factory.kind === 'integer');
    else if (factory.kind === 'string' && token.type === 'String') argument = literal(token.value);
    if (argument === null || (factory.argumentType === 'none' && Number(token.value) !== 0)) continue;
    const atom = numeric ? { nodeId: compactNumericIdentity(map, factory), magnitude: Number(token.value), unit: factory.unitName ?? '' } : { nodeId: factory.nodeId };
    if (!compactAdmits(map, factory.nodeId, atom) || !compactAdmits(map, property.nodeId, atom)) continue;
    if (factory.argumentType === 'none') return call(factory);
    return '(' + call(factory) + ' ' + (argument.startsWith('-') ? '(' + argument + ')' : argument) + ')';
  }
  return null;
}

function rangesAllow(value, unit, ranges) {
  const factors = { deg: [1, 'angle'], rad: [180 / Math.PI, 'angle'], turn: [360, 'angle'], grad: [.9, 'angle'], s: [1, 'time'], ms: [.001, 'time'], px: [1, 'length'], in: [96, 'length'], cm: [96 / 2.54, 'length'], mm: [96 / 25.4, 'length'], pt: [96 / 72, 'length'], pc: [16, 'length'] };
  for (const range of ranges) {
    for (const [bound, lower] of [[range.minimum, true], [range.maximum, false]]) {
      if (bound.kind !== 'finite') continue;
      let comparable = value;
      if (bound.unitName && bound.unitName !== unit) {
        const from = factors[unit], to = factors[bound.unitName];
        if (from && to && from[1] === to[1]) comparable = value * from[0] / to[0];
        else if (value !== 0) return false;
      }
      if (lower ? comparable < Number(bound.value) : comparable > Number(bound.value)) return false;
    }
  }
  return true;
}

function typedValue(value, property, map) {
  let tree;
  try { tree = parseValue(value, { context: 'value', onParseError(error) { throw error; } }); }
  catch { return null; }
  const nodes = tree.children.toArray();
  if (nodes.length !== 1) return null;
  const token = nodes[0];
  if (map.compact) return compactTypedValue(token, property, map);
  if (token.type === 'Identifier') {
    const keyword = ident.decode(token.name).toLowerCase();
    const helper = map.keywords.get(keyword);
    if (helper && property.acceptedKeywords.includes(keyword)) return 'Css.' + identifier(helper.memberName);
  }
  for (const primitive of map.primitives) {
    if (!property.acceptedMarkers.includes(primitive.marker)) continue;
    const conversion = property.acceptedValues?.find(item => item.marker === primitive.marker);
    if (!conversion || !Array.isArray(conversion.ranges)) continue;
    let argument = null;
    if (token.type === 'Dimension' && ['length', 'angle', 'time', 'frequency', 'resolution', 'flex'].includes(primitive.production)
        && primitive.methodName.toLowerCase() === ident.decode(token.unit).toLowerCase()) {
      argument = numberLiteral(token.value, false);
    } else if (token.type === 'Percentage' && ['percentage', 'percentage-token'].includes(primitive.production)) {
      argument = numberLiteral(token.value, false);
    } else if (token.type === 'Number' && ['number', 'number-token', 'integer'].includes(primitive.production)) {
      argument = numberLiteral(token.value, primitive.argumentType === 'int');
    } else if (token.type === 'String' && ['string', 'string-token'].includes(primitive.production)) {
      argument = literal(token.value);
    }
    if (argument !== null) {
      const unit = token.type === 'Dimension' ? primitive.methodName : token.type === 'Percentage' ? '%' : '';
      if (token.type !== 'String' && !rangesAllow(Number(token.value), unit, conversion.ranges)) continue;
      return '(Css.' + identifier(primitive.memberName) + ' ' + (argument.startsWith('-') ? '(' + argument + ')' : argument) + ')';
    }
  }
  return null;
}

function validProperty(name) {
  return /^--[A-Za-z0-9_-]+$|^-?[A-Za-z_][A-Za-z0-9_-]*$/.test(name);
}

/** Parses CSS and emits a portable F# rule module plus source-located fallback coverage. */
export function generate(css, manifest, options = {}) {
  const moduleName = qualified(options.moduleName ?? 'ImportedStyles');
  const map = prepare(manifest);
  const root = postcss.parse(css, { from: options.from });
  const report = { schemaVersion: 1, inputSha256: createHash('sha256').update(css).digest('hex'), typedDeclarations: 0, rawDeclarations: 0, fallbacks: [] };
  const diagnostic = (node, kind, reason) => report.fallbacks.push({ kind, reason, line: node.source?.start?.line ?? 1, column: node.source?.start?.column ?? 1 });
  const raw = (node, reason) => {
    if (node.type !== 'comment') {
      diagnostic(node, 'rule', reason);
      node.walkDecls?.(() => report.rawDeclarations++);
    }
    const start = node.source?.start?.offset, end = node.source?.end?.offset;
    const tokens = Number.isInteger(start) && Number.isInteger(end) ? node.source.input.css.slice(start, end) : node.toString();
    return 'Rule.unsafeFromText ' + literal(tokens);
  };
  const declaration = (node, typed) => {
    const tokens = node.raws.value?.raw ?? node.value;
    const property = typed ? map.properties.get(node.prop.toLowerCase()) : null;
    const value = property && !tokens.includes('/*') ? typedValue(tokens, property, map) : null;
    let result;
    if (value !== null) {
      result = 'Css.' + identifier(property.memberName) + ' ' + value;
      report.typedDeclarations++;
    } else {
      result = 'Declaration.create ' + literal(node.prop) + ' ' + literal(tokens);
      report.rawDeclarations++;
      diagnostic(node, 'declaration', typed ? 'No lossless typed conversion for this property/value.' : 'Descriptor declarations retain their original tokens.');
    }
    return node.important ? '(' + result + ' |> Declaration.important)' : result;
  };
  const list = (items, indent) => items.length === 0 ? '[]' : '[\n' + items.map(item => ' '.repeat(indent + 4) + item).join('\n') + '\n' + ' '.repeat(indent) + ']';
  const declarations = (nodes, indent, typed) => list(nodes.map(node => declaration(node, typed)), indent);
  const selector = text => {
    if (/^\.[A-Za-z_][A-Za-z0-9_-]*$/.test(text)) return 'Selector.className ' + literal(text.slice(1));
    if (/^#[A-Za-z_][A-Za-z0-9_-]*$/.test(text)) return 'Selector.id ' + literal(text.slice(1));
    if (/^[A-Za-z_][A-Za-z0-9_-]*$/.test(text)) return 'Selector.element ' + literal(text);
    return 'Selector.unsafeFromText ' + literal(text);
  };
  function rule(node, indent = 4, styleContext = false) {
    if (node.type === 'comment') return raw(node);
    if (node.type !== 'rule' && node.type !== 'atrule') return raw(node, 'Unsupported parsed node.');
    if (node.type === 'atrule' && node.raws.afterName === '' && node.params.startsWith('\\')) return raw(node, 'Preserves escaped at-rule heads.');
    if (node.type === 'rule' && /[{};]/.test(node.selector)) return raw(node, 'Selector exceeds the portable selector factory subset.');
    if (node.type === 'atrule' && !/^[A-Za-z_][A-Za-z0-9_-]*$/.test(node.name)) return raw(node, 'Escaped at-rule name exceeds the portable factory subset.');
    const opaqueHead = node.type === 'atrule' && /[{};]/.test(node.params);
    if (!node.nodes) return opaqueHead ? 'Rule.StatementRule (' + literal(node.name) + ', ' + literal(node.params) + ')' : 'Rule.statement ' + literal(node.name) + ' ' + literal(node.params);
    if (node.nodes.some(child => child.type === 'comment') && node.nodes.some(child => child.type === 'decl')) return raw(node, 'Preserves comments within declaration blocks.');
    if (node.nodes.some(child => child.type === 'decl' && !validProperty(child.prop))) return raw(node, 'Escaped or unsupported declaration name.');
    if (node.nodes.some(child => child.type === 'decl' && (/\S/.test(child.raws.before ?? '') || (child.raws.between ?? ':').replace(/[\t\r\n\f ]/g, '') !== ':'))) return raw(node, 'Preserves declaration hacks or nonstandard punctuation.');
    if (node.nodes.some(child => child.type === 'decl' && [child.raws.between, child.raws.important].some(text => text?.includes('/*')))) return raw(node, 'Preserves comments within declaration punctuation.');
    if (node.type === 'rule') {
      const firstChild = node.nodes.findIndex(child => child.type !== 'decl');
      if (firstChild >= 0 && node.nodes.slice(firstChild).some(child => child.type === 'decl')) return raw(node, 'Preserves declarations interleaved with nested rules.');
      const head = firstChild < 0 ? node.nodes : node.nodes.slice(0, firstChild);
      const children = firstChild < 0 ? [] : node.nodes.slice(firstChild);
      const prefix = children.length ? 'Rule.nested' : 'Rule.style';
      return prefix + ' (' + selector(node.selector) + ') ' + declarations(head, indent, true)
        + (children.length ? ' ' + list(children.map(child => rule(child, indent + 4, true)), indent) : '');
    }
    if (node.nodes.every(child => child.type === 'decl')) {
      const typed = styleContext && ['media', 'supports', 'container', 'layer', 'starting-style'].includes(node.name.toLowerCase());
      const body = declarations(node.nodes, indent, typed);
      return opaqueHead ? 'Rule.DescriptorRule (' + literal(node.name) + ', ' + literal(node.params) + ', ' + body + ')' : 'Rule.descriptors ' + literal(node.name) + ' ' + literal(node.params) + ' ' + body;
    }
    if (node.nodes.some(child => child.type === 'decl')) return raw(node, 'Preserves mixed at-rule contents.');
    const children = list(node.nodes.map(child => rule(child, indent + 4, styleContext)), indent);
    if (opaqueHead) return 'Rule.GroupRule (' + literal(node.name) + ', ' + literal(node.params) + ', ' + children + ')';
    if (['media', 'supports', 'container'].includes(node.name)) return 'Rule.' + node.name + ' ' + literal(node.params) + ' ' + children;
    if (node.name === 'layer') return 'Rule.layer ' + (node.params ? '(Some ' + literal(node.params) + ')' : 'None') + ' ' + children;
    return 'Rule.group ' + literal(node.name) + ' ' + literal(node.params) + ' ' + children;
  }
  const rules = root.nodes.map(node => rule(node));
  if (options.strictTyped && report.fallbacks.length) throw new Error(`Strict typed import rejected ${report.fallbacks.length} fallback(s); first at ${report.fallbacks[0].line}:${report.fallbacks[0].column}: ${report.fallbacks[0].reason}`);
  const imports = 'open Ranvier.Css\n' + (map.namespaceName === 'Ranvier.Css' ? '' : 'open ' + qualified(map.namespaceName) + '\n');
  const source = '// <auto-generated />\nmodule ' + moduleName + '\n\n' + imports + '\n/// <summary>Imported stylesheet rules in source order; explicit escapes retain unsupported syntax.</summary>\nlet rules: Rule list = ' + list(rules, 0) + '\n';
  return { source, report };
}

async function atomicWrite(target, contents) {
  await fs.mkdir(path.dirname(target), { recursive: true });
  const temporary = target + '.' + randomUUID() + '.tmp';
  try { await fs.writeFile(temporary, contents, 'utf8'); await fs.rename(temporary, target); }
  finally { await fs.rm(temporary, { force: true }); }
}

/** Runs file import; validation and strict-mode failures preserve existing output files. */
export async function run(args) {
  const settings = { manifest: path.resolve(directory, '../../src/Ranvier.Css.Bindings/Generated/Css.manifest.json') };
  for (let index = 0; index < args.length; index++) {
    const option = args[index];
    if (option === '--help') { process.stdout.write('CSS to Ranvier.Css: --input file.css --output file.fs [--module ImportedStyles] [--manifest bindings.manifest.json] [--report report.json] [--strict-typed]\n'); return 0; }
    if (option === '--strict-typed') { settings.strictTyped = true; continue; }
    const key = { '--input': 'input', '--output': 'output', '--module': 'moduleName', '--manifest': 'manifest', '--report': 'report' }[option];
    if (!key || index + 1 >= args.length || args[index + 1].startsWith('--')) throw new Error('Unknown option or missing value: ' + option);
    settings[key] = args[++index];
  }
  if (!settings.input || !settings.output) throw new Error('--input and --output are required.');
  settings.report ??= settings.output.replace(/\.fs$/i, '') + '.report.json';
  const paths = [settings.input, settings.manifest, settings.output, settings.report].map(value => path.resolve(value).toLowerCase());
  if (new Set(paths).size !== paths.length) throw new Error('Inputs, F# output, and report must have distinct paths.');
  const result = generate(await fs.readFile(settings.input, 'utf8'), JSON.parse(await fs.readFile(settings.manifest, 'utf8')), { ...settings, from: settings.input });
  await atomicWrite(path.resolve(settings.output), result.source);
  await atomicWrite(path.resolve(settings.report), JSON.stringify(result.report, null, 2) + '\n');
  process.stdout.write(`${result.report.typedDeclarations} typed declarations, ${result.report.fallbacks.length} explicit fallbacks.\n`);
  return 0;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try { process.exitCode = await run(process.argv.slice(2)); }
  catch (error) { process.stderr.write(error.message + '\n'); process.exitCode = 1; }
}
