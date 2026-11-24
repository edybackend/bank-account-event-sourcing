// PreToolUse hook: stored events are a contract. Removing or renaming an event type or one of its
// fields would make existing streams unreadable, so such an edit needs a human decision (upcasting).
import { readFileSync } from 'node:fs'

const input = JSON.parse(readFileSync(0, 'utf8'))
const filePath = (input.tool_input?.file_path ?? '').replaceAll('\\', '/')
if (!filePath.endsWith('src/BankAccount.Domain/AccountEvents.cs')) process.exit(0)

const before = (() => {
  try {
    return readFileSync(filePath, 'utf8')
  } catch {
    return ''
  }
})()
const oldText = input.tool_input?.old_string
const after = oldText !== undefined ? before.replace(oldText, input.tool_input?.new_string ?? '') : (input.tool_input?.content ?? '')

// "record Name(field list)" → set of "Name.Field"
const members = (source) =>
  new Set(
    [...source.matchAll(/record\s+(\w+)\s*\(([^)]*)\)/g)].flatMap(([, record, args]) =>
      args.split(',').map((arg) => `${record}.${arg.trim().split(/\s+/).pop()}`),
    ),
  )

const removed = [...members(before)].filter((member) => !members(after).has(member))
if (removed.length > 0) {
  console.error(
    `Blocked: this edit removes or renames stored event members (${removed.join(', ')}). ` +
      'Old events would no longer deserialize. Add a new field or a new event version instead, or ask the developer.',
  )
  process.exit(2)
}
