import assert from 'node:assert/strict';
import test from 'node:test';
import fs from 'node:fs';
import { generate } from './index.mjs';

export const manifest = {
  sugar: {
    schemaVersion: 2,
    namespaceName: 'Ranvier.Css',
    properties: [
      { cssName: 'width', memberName: 'width', scopes: [], acceptedMarkers: ['Length'], acceptedKeywords: ['auto'], obsolete: false },
      { cssName: 'opacity', memberName: 'opacity', scopes: [], acceptedMarkers: ['Number'], acceptedKeywords: [], obsolete: false },
      { cssName: 'display', memberName: 'display', scopes: [], acceptedMarkers: [], acceptedKeywords: ['block'], obsolete: false },
      { cssName: 'color', memberName: 'color', scopes: [], acceptedMarkers: [], acceptedKeywords: ['red'], obsolete: false }
    ],
    primitives: [
      { memberName: 'px', marker: 'Length', production: 'length', methodName: 'px', argumentType: 'float' },
      { memberName: 'number', marker: 'Number', production: 'number', methodName: 'create', argumentType: 'float' }
    ],
    keywords: ['auto', 'block', 'red'].map(token => ({ token, memberName: token }))
  }
};

for (const property of manifest.sugar.properties) property.acceptedValues = property.acceptedMarkers.map(marker => ({ marker, ranges: [] }));

test('real CSS declarations become typed sugar, retaining fallbacks and importance', () => {
  const result = generate('.card { width: 240px; display: block; color: red !important; width: auto; }', manifest);
  assert.match(result.source, /Css\.width \(Css\.px 240\.0\)/);
  assert.match(result.source, /Css\.display Css\.block/);
  assert.match(result.source, /Css\.color Css\.red.*Declaration\.important/);
  assert.ok(result.source.indexOf('240.0') < result.source.indexOf('Css.auto'));
  assert.equal(result.report.typedDeclarations, 4);
  assert.equal(result.report.fallbacks.length, 0);
});

test('custom properties and complex values remain explicit and ordered', () => {
  const result = generate('.card { --width: 10px; width: calc(var(--width) * 2); }', manifest);
  assert.match(result.source, /Declaration\.create "--width" "10px"/);
  assert.match(result.source, /calc\(var\(--width\) \* 2\)/);
  assert.equal(result.report.fallbacks.length, 2);
  assert.throws(() => generate('.card { width: calc(10px * 2); }', manifest, { strictTyped: true }), /typed/i);
});

test('nested conditions retain rule order; interleaved declarations are never hoisted', () => {
  const result = generate('@media (width > 1px) { .card { width: 2px; &:hover { width: 3px; } } }', manifest);
  assert.match(result.source, /Rule\.media/);
  assert.match(result.source, /Rule\.nested/);
  const mixed = generate('.a { width: 1px; &:hover { width: 2px; } width: 3px; }', manifest);
  assert.match(mixed.source, /Rule\.unsafeFromText/);
  assert.ok(mixed.report.fallbacks.some(item => item.kind === 'rule'));
});

test('syntax errors fail and cannot produce partial output', () => {
  assert.throws(() => generate('.card { width: 2px;', manifest), /unclosed/i);
});

test('comments, escaped selectors, and unknown at-rule bodies are preserved', () => {
  const result = generate('/*! license */ [data-x=";"] { width: 2px; } @future x { token { x: y } tail: z; }', manifest);
  assert.match(result.source, /license/);
  assert.match(result.source, /Rule\.unsafeFromText/);
  assert.match(result.source, /@future/);
});

test('F# literals escape control characters and names cannot inject source', () => {
  const result = generate('.a { --x: "line\\a break"; }', manifest);
  assert.match(result.source, /\\\\a/);
  assert.throws(() => generate('.a {}', manifest, { moduleName: 'Bad\nlet x = 1' }), /identifier/i);
});

test('numbers that would lose source precision use an explicit fallback', () => {
  const result = generate('.a { width: 9007199254740993px; opacity: .5; }', manifest);
  assert.equal(result.report.typedDeclarations, 1);
  assert.match(result.source, /9007199254740993px/);
});

test('inline comments, escaped CSS tokens and non-ASCII property names survive explicitly', () => {
  const result = generate('.a { width: 2px/*keep*/; --x: "\\n"; --🍏: 1px; }', manifest);
  assert.match(result.source, /Rule\.unsafeFromText/);
  assert.match(result.source, /\\\\n/);
  const valueComments = generate('.a { width: 2px/*keep*/; }', manifest);
  assert.match(valueComments.source, /keep/);
  assert.equal(valueComments.report.typedDeclarations, 0);
});

test('quoted stylesheet delimiters in at-rule heads remain structured', () => {
  const result = generate('@import url("a;b.css"); @import url("b.css");', manifest);
  assert.match(result.source, /Rule\.StatementRule/);
  assert.equal(result.report.fallbacks.length, 0);
});

test('canonical numeric range guards force fallback rather than generate a throwing module', () => {
  const guarded = structuredClone(manifest);
  guarded.sugar.properties[0].acceptedValues[0].ranges = [{ minimum: { kind: 'finite', value: '0', unitName: '' }, maximum: { kind: 'finite', value: '100', unitName: '' } }];
  const result = generate('.a { width: -1px; width: 101px; width: 42px; }', guarded);
  assert.equal(result.report.typedDeclarations, 1);
  assert.equal(result.report.fallbacks.length, 2);
  assert.throws(() => generate('.a { width: -1px; }', guarded, { strictTyped: true }), /Strict typed/);
});

test('escaped at-rule heads retain their tokens and fail strict conversion explicitly', () => {
  for (const css of ['@im\\port url(a.css);', '@m\\65 dia screen { .a { width: 2px; } }']) {
    const result = generate(css, manifest);
    assert.match(result.source, /Rule\.unsafeFromText/);
    assert.equal(result.report.fallbacks[0].kind, 'rule');
    if (css.startsWith('@im')) assert.match(result.source, /url\(a\.css\);/);
    assert.throws(() => generate(css, manifest, { strictTyped: true }), /Strict typed/);
  }
});

test('custom sugar namespaces still import the portable core first', () => {
  const custom = structuredClone(manifest);
  custom.sugar.namespaceName = 'Example';
  assert.match(generate('.a { width: 2px; }', custom).source, /open Ranvier\.Css\nopen Example/);
});

test('legacy declaration hacks cannot silently become ordinary typed declarations', () => {
  const result = generate('.a { *width: 42px; }', manifest);
  assert.match(result.source, /Rule\.unsafeFromText/);
  assert.match(result.source, /\*width/);
  assert.equal(result.report.typedDeclarations, 0);
});

function compactManifest() {
  const node = (id, kind, text = '', children = [], target = -1, minimum = 0, maximum = -1, lower = '', upper = '', unit = '') =>
    [String(id), String(kind), text, String(target), children.join(','), '', String(minimum), String(maximum), '0', lower, unit, upper, unit, ''];
  const nodes = [
    node(0, 0, 'length'), node(1, 2, 'length', [], 0, 0, -1, '0', '100'),
    node(2, 1, 'auto'), node(3, 3, '', [1, 2]), node(4, 7, '', [1], -1, 2, 3),
    node(5, 7, '', [2], -1, 0, 1), node(6, 4, '', [5, 1]), node(7, 4, '', [2, 1]),
    node(8, 0, 'time'), node(9, 0, 'integer'), node(10, 0, 'number'), node(11, 0, 'string'),
    node(12, 0, 'percentage'), node(13, 2, 'percentage', [], 12, 0, -1, '0', '100'),
    node(14, 0, 'unresolved'), node(15, 2, 'cycle', [], 15), node(16, 3, '', [15, 1]),
    node(17, 2, 'length', [], 0, 0, -1, '0', '2', 'cm'), node(18, 1, 'block'),
    node(19, 1, 'red'), node(20, 1, 'initial'), node(21, 2, 'integer', [], 9, 0, -1, '1', '1000')
  ];
  const field = value => value.length + ':' + value;
  const property = (cssName, nodeId) => ({ cssName, nodeId, owner: 'Example.Compact.Css', memberName: cssName });
  const factory = (owner, memberName, nodeId, kind, production = '', unitName = '', argumentType = 'none') =>
    ({ owner: 'Example.Compact.' + owner, memberName, nodeId, kind, production, unitName, argumentType });
  return {
    schemaVersion: 2, namespaceName: 'Example.Compact',
    encodedGrammar: [String(nodes.length), ...nodes.flat()].map(field).join(''),
    properties: [property('width', 3), property('repeat', 4), property('optional', 6), property('pair', 7), property('opacity', 10), property('weight', 21), property('display', 18), property('color', 19), property('font', 11), property('cycle', 16), property('physical', 17), property('unknown', 14)],
    owners: [{ nodeId: 18, path: ['Display'] }, { nodeId: 19, path: ['Color'] }],
    factories: [
      factory('Css', 'px', 0, 'number', 'length', 'px', 'float'),
      factory('Css', 'cm', 0, 'number', 'length', 'cm', 'float'),
      factory('Css', 'rem', 0, 'number', 'length', 'rem', 'float'),
      factory('Css', 'number', 10, 'number', 'number', '', 'float'),
      factory('Css', 'integer', 9, 'integer', 'integer', '', 'int'),
      factory('Css', 'string', 11, 'string', 'string', '', 'string'),
      factory('CssValue.Width', 'auto', 2, 'token'), factory('CssValue.Display', 'block', 18, 'token'),
      factory('CssValue.Color', 'red', 19, 'token'), factory('CssValue.All', 'initial', 20, 'wide')
    ]
  };
}

test('compact manifests emit actual property-owned keyword and primitive factories', () => {
  const result = generate('.a { width:42px; display:block; color:red!important; width:auto; width:initial; font:"hello"; }', compactManifest());
  assert.equal(result.report.typedDeclarations, 6);
  assert.match(result.source, /open Ranvier\.Css\nopen Example\.Compact/);
  assert.match(result.source, /Css\.width \(Css\.px 42\.0\)/);
  assert.match(result.source, /Css\.display CssValue\.Display\.block/);
  assert.match(result.source, /Css\.color CssValue\.Color\.red/);
  assert.match(result.source, /Css\.width CssValue\.All\.initial/);
  assert.match(result.source, /Css\.font \(Css\.string "hello"\)/);
});

test('compact atomic admission preserves required siblings cardinalities and recursion', () => {
  const result = generate('.a { repeat:1px; optional:1px; pair:1px; cycle:1px; unknown:1px; }', compactManifest());
  assert.equal(result.report.typedDeclarations, 2);
  assert.match(result.source, /Css\.optional \(Css\.px 1\.0\)/);
  assert.match(result.source, /Css\.cycle \(Css\.px 1\.0\)/);
  for (const name of ['repeat', 'pair', 'unknown']) assert.ok(result.source.includes('Declaration.create "' + name + '"'));
});

test('compact numeric guards retain physical conversions unknown units precision and integer limits', () => {
  const result = generate('.a { width:-1px; width:101px; width:42px; physical:96px; physical:1cm; physical:1rem; physical:0rem; weight:1001; weight:1.5; weight:500; weight:2147483648; opacity:9007199254740993; }', compactManifest());
  assert.equal(result.report.typedDeclarations, 4);
  assert.match(result.source, /Css\.physical \(Css\.cm 1\.0\)/);
  assert.match(result.source, /Css\.physical \(Css\.rem 0\.0\)/);
  assert.match(result.source, /Css\.weight \(Css\.integer 500\)/);
  assert.throws(() => generate('.a { width:-1px; }', compactManifest(), { strictTyped: true }), /Strict typed/);
});

test('compact malformed grammar and missing edges fail before source emission', () => {
  const manifest = compactManifest();
  assert.throws(() => generate('.a {}', { ...manifest, encodedGrammar: '2:99' }), /grammar/);
  assert.throws(() => generate('.a {}', { ...manifest, encodedGrammar: manifest.encodedGrammar.replace('6:length', '9:length') }), /grammar/);
  assert.throws(() => generate('.a {}', { ...manifest, properties: [{ ...manifest.properties[0], nodeId: 999 }] }), /property node/);
});

test('checked-in compact manifest supports the default atomic authoring API and bounded fallback', () => {
  const manifest = JSON.parse(fs.readFileSync(new URL('../../src/Ranvier.Css.Bindings/Generated/Css.manifest.json', import.meta.url), 'utf8'));
  assert.equal(manifest.schemaVersion, 2);
  const result = generate('.a { width:42px; width:50%; display:block; color:red; width:-1px; font-weight:1001; width:calc(1px * 2); }', manifest);
  assert.equal(result.report.typedDeclarations, 4);
  assert.match(result.source, /CssValue\.Display\.block/);
  assert.match(result.source, /CssValue\.Color\.red/);
  assert.equal(result.report.rawDeclarations, 3);
});
