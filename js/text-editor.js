// Markdown formatting helper — inserts text at cursor position

export function insertFormatting(textarea, before, after, placeholder) {
    const start = textarea.selectionStart;
    const end = textarea.selectionEnd;
    const selected = textarea.value.substring(start, end);
    const text = selected || placeholder;
    const insertion = before + text + after;

    textarea.setRangeText(insertion, start, end, 'select');
    const newStart = start + before.length;
    const newEnd = newStart + text.length;
    textarea.setSelectionRange(newStart, newEnd);
    textarea.focus();
    textarea.dispatchEvent(new Event('input', { bubbles: true }));
}
