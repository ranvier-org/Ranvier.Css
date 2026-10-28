import { test } from 'node:test';
import assert from 'node:assert/strict';
import { assertCompilerRejections, rejectionProject } from './compiler-rejections.mjs';

const cases = [
  { name: 'AngleWidth', code: 'FS0001' },
  { name: 'WrongOverload', code: 'FS0041' },
  { name: 'NumericDimension', code: ['FS0001', 'FS0043'] },
  { name: 'ArchivedApi', code: 'FS0044' },
];
const diagnostics = [
  'C:\\checks\\AngleWidth.fs(3,15): error FS0001: Type mismatch [C:\\checks\\Negative.fsproj]',
  '/checks/WrongOverload.fs(3,15): error FS0041: No overloads match [/checks/Negative.fsproj]',
  '/checks/NumericDimension.fs(3,15): error FS0043: Incompatible units [/checks/Negative.fsproj]',
  '/checks/ArchivedApi.fs(3,15): error FS0044: Deprecated [/checks/Negative.fsproj]',
].join('\n');

test('matches each expected diagnostic to its own source file on either platform', () => {
  assert.doesNotThrow(() => assertCompilerRejections(cases, diagnostics));
});
test('a different file or unrelated build failure cannot satisfy a missing case', () => {
  assert.throws(() => assertCompilerRejections(cases, diagnostics.replace('AngleWidth.fs', 'Other.fs')), /AngleWidth/);
  assert.throws(() => assertCompilerRejections(cases, 'error MSB1009: project missing'), /AngleWidth/);
});
test('a wrong compiler code or warning severity does not satisfy a case', () => {
  assert.throws(() => assertCompilerRejections(cases, diagnostics.replace('error FS0001', 'error FS0039')), /AngleWidth/);
  assert.throws(() => assertCompilerRejections(cases, diagnostics.replace('error FS0044', 'warning FS0044')), /ArchivedApi/);
});
test('summary duplicates cannot hide a missing case or misattribute a similarly named file', () => {
  const incomplete = diagnostics.split('\n').slice(0, 3).join('\n');
  assert.throws(() => assertCompilerRejections(cases, incomplete + '\n' + incomplete), /ArchivedApi/);
  assert.throws(() => assertCompilerRejections(cases, diagnostics.replace('ArchivedApi.fs(', 'PrefixArchivedApi.fs(')), /ArchivedApi/);
});
test('batch project compiles every named file and raises the error cap above the complete batch', () => {
  const project = rejectionProject('C:/repo/Bindings.fsproj', cases);
  for (const item of cases) assert.match(project, new RegExp(`<Compile Include="${item.name}\\.fs"/>`));
  assert.match(project, /--maxerrors:1000/);
  assert.match(project, /<WarningsAsErrors>44<\/WarningsAsErrors>/);
  assert.throws(() => rejectionProject('bindings.fsproj', [cases[0], cases[0]]), /Duplicate/);
});
