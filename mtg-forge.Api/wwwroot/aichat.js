let aiSessionId = null;
let aiCurrentDeckId = null;

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
        if (aiCurrentDeckId && !aiSessionId) payload.deckId = aiCurrentDeckId;

        const headers = { 'Content-Type': 'application/json' };
        if (typeof authToken !== 'undefined' && authToken) {
            headers['Authorization'] = 'Bearer ' + authToken;
        }

        const res = await fetch('/api/ai/chat/brew', {
            method: 'POST',
            headers: headers,
            body: JSON.stringify(payload)
        });

        if (res.ok) {
            const data = await res.json();
            aiSessionId = data.sessionId;
            document.getElementById(loadingId).remove();
            appendAiMessage('assistant', data.reply, data.actions);
        } else {
            const errText = await res.text().catch(() => '');
            document.getElementById(loadingId).innerText = `Error reaching AI (${res.status}). ${errText}`;
        }
    } catch (e) {
        document.getElementById(loadingId).innerText = 'Error reaching AI: ' + (e && e.message ? e.message : e);
    }
}

function appendAiMessage(role, content, actions = null) {
    const isUser = role === 'user';
    let actionsHtml = '';
    
    if (actions && actions.length > 0) {
        const buttons = actions.map(act => {
            const actJson = escHtml(JSON.stringify(act));
            return `<button data-action="${actJson}" onclick="executeAiAction(this)" style="margin-top:0.5rem; margin-right:0.5rem; padding:0.4rem 0.75rem; background:var(--bg-hover); color:var(--mana-blue); border:1px solid var(--mana-blue); border-radius:12px; font-size:0.8rem; cursor:pointer; font-weight:bold;">✨ ${escHtml(act.label)}</button>`;
        }).join('');
        actionsHtml = `<div style="margin-top:0.75rem;">${buttons}</div>`;
    }

    const html = `
        <div style="margin-bottom:1rem; padding:0.75rem; border-radius:8px; background:${isUser ? 'var(--bg-hover)' : 'var(--bg-panel)'}; border:${isUser ? 'none' : '1px solid var(--border)'}">
            <div style="font-weight:bold; color:var(--text-secondary); margin-bottom:0.25rem;">${isUser ? 'You' : 'Forge AI'}</div>
            <div style="color:var(--text-primary); line-height:1.4;">${marked.parse(content)}</div>
            ${actionsHtml}
        </div>
    `;
    const log = document.getElementById('aiChatLog');
    log.insertAdjacentHTML('beforeend', html);
    log.scrollTop = log.scrollHeight;
}

// Ensure escaping utility is available
function escHtml(s) {
    return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;').replace(/'/g,'&#39;');
}

async function executeAiAction(btnEl) {
    if (!aiCurrentDeckId) return;
    try {
        const actionJson = btnEl.getAttribute('data-action');
        btnEl.innerText = "⏳ Applying...";
        btnEl.disabled = true;
        
        const act = JSON.parse(actionJson);
        const headers = { 'Content-Type': 'application/json' };
        if (typeof authToken !== 'undefined' && authToken) {
            headers['Authorization'] = 'Bearer ' + authToken;
        }

        let url = '';
        let payload = {};

        if (act.type === 'add' && act.addCard) {
            url = `/api/decks/${aiCurrentDeckId}/add-card`;
            payload = { cardName: act.addCard };
        } else if (act.type === 'swap' && act.addCard && act.removeCard) {
            url = `/api/decks/${aiCurrentDeckId}/apply-upgrade`;
            payload = { removeCard: act.removeCard, addCard: act.addCard, reason: "AI Suggestion" };
        } else if (act.type === 'reply') {
            btnEl.style.display = 'none';
            document.getElementById('aiChatInput').value = act.message || act.label;
            sendAiBrewMessage();
            return;
        } else {
            btnEl.innerText = "❌ Unknown Action";
            return;
        }

        const res = await fetch(url, {
            method: 'POST',
            headers: headers,
            body: JSON.stringify(payload)
        });

        if (res.ok) {
            btnEl.innerText = "✅ Applied";
            btnEl.style.color = "var(--mana-green)";
            btnEl.style.borderColor = "var(--mana-green)";

            // Refresh the main UI
            if (typeof loadDeckDetails === 'function') {
                await loadDeckDetails(aiCurrentDeckId);
            } else if (typeof authFetch === 'function') {
                // Inline fetch & render
                const deckRes = await authFetch(`/api/decks/${aiCurrentDeckId}`);
                if (deckRes.ok) {
                    const deck = await deckRes.json();
                    if (typeof renderDetail === 'function') {
                        renderDetail(deck);
                    }
                }
            }

            // Tell AI silently
            const sysMsg = `System: User accepted your suggestion to ${act.label}.`;
            appendAiMessage('user', `*Accepted suggestion: ${act.label}*`);
            
            // Optionally we could submit this silently to the AI so the context knows.
        } else {
            const errText = await res.text().catch(() => '');
            btnEl.innerText = "❌ Failed";
            console.error("Action error:", errText);
        }
    } catch (e) {
        btnEl.innerText = "❌ Error";
        console.error(e);
    }
}
