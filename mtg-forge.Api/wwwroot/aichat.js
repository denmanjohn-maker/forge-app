let aiSessionId = null;

async function sendAiBrewMessage(event) {
    if (event) event.preventDefault();
    const input = document.getElementById('aiChatInput');
    const msg = input.value.trim();
    if (!msg) return;

    appendAiMessage('user', msg);
    input.value = '';
    
    // show loading
    const loadingId = 'loading-' + Date.now();
    document.getElementById('aiChatLog').insertAdjacentHTML('beforeend', `<div id="${loadingId}" style="color:var(--text-muted);font-style:italic;">AI is brewing...</div>`);

    try {
        const payload = { prompt: msg };
        if (aiSessionId) payload.sessionId = aiSessionId;

        const res = await fetch('/api/ai/chat/brew', {
            method: 'POST',
            headers: getAuthHeaders(),
            body: JSON.stringify(payload)
        });

        if (res.ok) {
            const data = await res.json();
            aiSessionId = data.sessionId;
            document.getElementById(loadingId).remove();
            appendAiMessage('assistant', data.reply);
        } else {
            document.getElementById(loadingId).innerText = 'Error reaching AI.';
        }
    } catch (e) {
        document.getElementById(loadingId).innerText = 'Error reaching AI.';
    }
}

function appendAiMessage(role, content) {
    const isUser = role === 'user';
    const html = `
        <div style="margin-bottom:1rem; padding:0.75rem; border-radius:8px; background:${isUser ? 'var(--bg-hover)' : 'var(--bg-panel)'}; border:${isUser ? 'none' : '1px solid var(--border)'}">
            <div style="font-weight:bold; color:var(--text-secondary); margin-bottom:0.25rem;">${isUser ? 'You' : 'Forge AI'}</div>
            <div style="color:var(--text-primary); line-height:1.4;">${marked.parse(content)}</div>
        </div>
    `;
    const log = document.getElementById('aiChatLog');
    log.insertAdjacentHTML('beforeend', html);
    log.scrollTop = log.scrollHeight;
}
