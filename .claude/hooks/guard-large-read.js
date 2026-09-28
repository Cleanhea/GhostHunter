#!/usr/bin/env node
// PreToolUse(Read) — 큰 텍스트 에셋(.unity·.prefab·.asset)을 범위 없이 통째로 읽지 못하게 막는다.
// Stage1.unity(약 2.3MB)·ProtoTypeGame.unity(약 5.6MB)를 한 번 열면 수만 토큰이 든다.
// grep -n 으로 위치를 찾은 뒤 offset/limit 로 필요한 구간만 읽게 한다. 훅 자체 오류는 조용히 통과한다.
'use strict';

const fs = require('fs');
const path = require('path');

const EXTENSIONS = ['.unity', '.prefab', '.asset'];
const MAX_BYTES = 256 * 1024;
const MAX_LINES = 400;

function main() {
  const input = JSON.parse(fs.readFileSync(0, 'utf8') || '{}');
  const toolInput = input.tool_input || {};
  const filePath = toolInput.file_path || '';
  if (!EXTENSIONS.includes(path.extname(filePath).toLowerCase())) return;

  const limit = Number(toolInput.limit) || 0;
  if (limit > 0 && limit <= MAX_LINES) return;

  const root = path.resolve(process.env.CLAUDE_PROJECT_DIR || input.cwd || process.cwd());
  const abs = path.resolve(root, filePath);
  let size = 0;
  try {
    size = fs.statSync(abs).size;
  } catch {
    return;
  }
  if (size <= MAX_BYTES) return;

  const rel = path.relative(root, abs).split(path.sep).join('/');
  const reason =
    `${rel}는 ${(size / 1024 / 1024).toFixed(1)}MB 텍스트 에셋이다. 통째로 읽지 않는다 — ` +
    `grep -n 으로 대상(m_Name·fileID·guid·컴포넌트 이름)의 줄 번호를 찾은 뒤 ` +
    `Read offset/limit(${MAX_LINES}줄 이하)로 그 구간만 읽는다.`;
  process.stdout.write(JSON.stringify({
    hookSpecificOutput: {
      hookEventName: 'PreToolUse',
      permissionDecision: 'deny',
      permissionDecisionReason: reason,
    },
  }));
}

try {
  main();
} catch {
  // 훅 오류로 작업을 막지 않는다.
}
process.exit(0);
