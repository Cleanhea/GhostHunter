#!/usr/bin/env node
// PostToolUse(Edit|Write) — CLAUDE.md §3 코드 하드 룰 중 기계로 잡을 수 있는 것을 검사한다.
// HEAD 대비 새로 추가된 줄만 본다(추적되지 않은 파일은 전체). 기존 위반에는 반응하지 않는다.
// 주석과 문자열 리터럴은 검사에서 뺀다(소스 검사 테스트의 문자열 오탐 방지).
// 위반이 있으면 {"decision":"block"}으로 이유를 Claude에게 돌려준다. 훅 자체 오류는 조용히 통과한다.
'use strict';

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const STEAM_LAYER = 'Assets/Scripts/Systems/Steam/';
const SKIP_PREFIXES = ['Packages/', 'Assets/Plugins/', 'Assets/ThirdParty/', 'Library/', 'Temp/'];

// keepStrings: 문자열 인자가 핵심인 규칙(SendMessage("Method"))은 문자열을 남긴 줄로 검사한다.
const RULES = [
  {
    re: /\bInput\.(GetKey|GetKeyDown|GetKeyUp|GetAxis|GetAxisRaw|GetButton|GetButtonDown|GetButtonUp|GetMouseButton|GetMouseButtonDown|GetMouseButtonUp|mousePosition|mouseScrollDelta|anyKey|anyKeyDown|inputString|touchCount|GetTouch)\b/,
    msg: '레거시 Input.* 금지 — Input System(InputAction) 사용',
  },
  { re: /\basync\s+void\b/, msg: 'async void 금지 — async UniTaskVoid + .Forget()' },
  {
    re: /\bGameObject\.Find(WithTag|GameObjectsWithTag|GameObjectWithTag)?\s*\(/,
    msg: 'GameObject.Find 금지 — 직렬화 참조 또는 명시적 주입',
  },
  {
    re: /\b(SendMessage|SendMessageUpwards|BroadcastMessage)\s*\(\s*("|nameof\b)/,
    msg: 'SendMessage 계열 금지',
    keepStrings: true,
  },
  { re: /\[\s*(ServerRpc|ClientRpc)\b/, msg: '레거시 [ServerRpc]/[ClientRpc] 금지 — [Rpc(SendTo.…)]' },
  { re: /\bStartCoroutine\s*\(/, msg: '새 코루틴 금지 — UniTask' },
  { re: /^\s*namespace\s+[\w.]*\bDebug\b/, msg: '네임스페이스에 Debug 금지 — DebugTools' },
  {
    re: /\bstatic\b[^;=(){}]*\bInstance\s*(\{|;|=>|=)/,
    msg: '서비스의 static Instance 금지 — SceneInstaller에서 등록',
  },
  { re: /\bCamera\.main\b/, msg: '(확인) Camera.main — 매 프레임 호출 금지, 캐시하거나 주입' },
  {
    re: /^\s*using\s+Steamworks\b|\bSteamworks\./,
    msg: `Steamworks는 Steam 레이어(${STEAM_LAYER})에서만 — Gameplay·UI는 ISteamLobbyService`,
    allow: rel => rel.startsWith(STEAM_LAYER),
  },
];

function readStdin() {
  try {
    return fs.readFileSync(0, 'utf8');
  } catch {
    return '';
  }
}

// 주석을 지운 두 가지 사본(문자열 유지 / 문자열 내용 제거)을 줄 구조를 보존한 채 만든다.
function stripComments(text) {
  let withStrings = '';
  let noStrings = '';
  const emit = (ch, isString) => {
    withStrings += ch;
    noStrings += isString && ch !== '\n' ? '' : ch;
  };
  let i = 0;
  while (i < text.length) {
    const c = text[i];
    const next = text[i + 1];
    if (c === '/' && next === '/') {
      while (i < text.length && text[i] !== '\n') i++;
      continue;
    }
    if (c === '/' && next === '*') {
      i += 2;
      while (i < text.length && !(text[i] === '*' && text[i + 1] === '/')) {
        if (text[i] === '\n') emit('\n', false);
        i++;
      }
      i += 2;
      continue;
    }
    const verbatim = (c === '@' && (next === '"' || (next === '$' && text[i + 2] === '"'))) ||
      (c === '$' && next === '@' && text[i + 2] === '"');
    if (verbatim) {
      while (text[i] !== '"') emit(text[i++], false);
      emit('"', false);
      i++;
      while (i < text.length) {
        if (text[i] === '"' && text[i + 1] === '"') {
          emit('""', true);
          i += 2;
          continue;
        }
        if (text[i] === '"') break;
        emit(text[i++], true);
      }
      emit('"', false);
      i++;
      continue;
    }
    if (c === '"' || c === '\'') {
      const quote = c;
      emit(quote, false);
      i++;
      while (i < text.length && text[i] !== quote && text[i] !== '\n') {
        if (text[i] === '\\' && i + 1 < text.length) {
          emit(text[i] + text[i + 1], true);
          i += 2;
          continue;
        }
        emit(text[i++], true);
      }
      if (text[i] === quote) {
        emit(quote, false);
        i++;
      }
      continue;
    }
    emit(c, false);
    i++;
  }
  return { withStrings: withStrings.split('\n'), noStrings: noStrings.split('\n') };
}

// HEAD 대비 추가된 줄 번호(1부터). 추적되지 않은 파일이면 null(= 전체 검사).
function addedLines(root, rel) {
  try {
    execFileSync('git', ['ls-files', '--error-unmatch', '--', rel], { cwd: root, stdio: 'ignore' });
  } catch {
    return null;
  }
  const diff = execFileSync('git', ['diff', '--no-color', '--no-ext-diff', '-U0', 'HEAD', '--', rel], {
    cwd: root,
    encoding: 'utf8',
    maxBuffer: 32 * 1024 * 1024,
  });
  const lines = new Set();
  let inHunk = false;
  let current = 0;
  for (const raw of diff.split('\n')) {
    const line = raw.replace(/\r$/, '');
    const hunk = /^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@/.exec(line);
    if (hunk) {
      inHunk = true;
      current = Number(hunk[1]);
      continue;
    }
    if (!inHunk) continue;
    if (line.startsWith('+')) lines.add(current++);
  }
  return lines;
}

function main() {
  const input = JSON.parse(readStdin() || '{}');
  const filePath = (input.tool_input && input.tool_input.file_path) || '';
  if (!filePath.toLowerCase().endsWith('.cs')) return;

  const root = path.resolve(process.env.CLAUDE_PROJECT_DIR || input.cwd || process.cwd());
  const abs = path.resolve(root, filePath);
  const rel = path.relative(root, abs).split(path.sep).join('/');
  if (rel.startsWith('..') || path.isAbsolute(rel)) return;
  if (SKIP_PREFIXES.some(prefix => rel.startsWith(prefix))) return;
  if (!fs.existsSync(abs)) return;

  const text = fs.readFileSync(abs, 'utf8').replace(/^﻿/, '').replace(/\r\n/g, '\n');
  const { withStrings, noStrings } = stripComments(text);
  const added = addedLines(root, rel);
  const rawLines = text.split('\n');

  const violations = [];
  for (let index = 0; index < noStrings.length; index++) {
    const lineNo = index + 1;
    if (added && !added.has(lineNo)) continue;
    for (const rule of RULES) {
      if (rule.allow && rule.allow(rel)) continue;
      const source = rule.keepStrings ? withStrings[index] : noStrings[index];
      if (source && rule.re.test(source)) {
        violations.push(`  L${lineNo}: ${rule.msg}  | ${(rawLines[index] || '').trim().slice(0, 120)}`);
      }
    }
  }
  if (violations.length === 0) return;

  const reason = [
    `CLAUDE.md §3 코드 하드 룰 위반 의심 — ${rel} (이번에 추가된 줄만 검사):`,
    ...violations.slice(0, 20),
    violations.length > 20 ? `  … ${violations.length - 20}건 더` : '',
    '고친다. 의도된 예외라면 이유를 사용자에게 알린다.',
  ].filter(Boolean).join('\n');
  process.stdout.write(JSON.stringify({ decision: 'block', reason }));
}

try {
  main();
} catch {
  // 훅 오류로 작업을 막지 않는다.
}
process.exit(0);
