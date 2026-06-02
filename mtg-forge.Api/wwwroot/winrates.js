// WIN RATE JS SCRIPTS
function _forgeAuthHeaders(json) {
    const h = json ? { 'Content-Type': 'application/json' } : {};
    if (typeof authToken !== 'undefined' && authToken) {
        h['Authorization'] = 'Bearer ' + authToken;
    }
    return h;
}

async function fetchWinRates(deckId) {
    try {
        const res = await fetch(`/api/gamelogs/deck/${deckId}/stats`, { headers: _forgeAuthHeaders(false) });
        if (res.ok) {
            const stats = await res.json();
            if (stats) {
                renderWinRateStats(stats);
                return;
            }
        }
    } catch(e) { }
    document.getElementById('winRateStats').innerHTML = '<p class="text-secondary">No games played yet. Log games to track win rate.</p>';
}

function renderWinRateStats(stats) {
    const rate = Math.round(stats.winRate * 100);
    const html = `
        <div style="display:flex; justify-content:space-around; background:var(--bg-panel); padding:1rem; border-radius:8px;">
            <div style="text-align:center;">
                <div style="font-size:2rem; color:${rate > 50 ? 'var(--success)' : (rate < 50 ? 'var(--danger)' : 'var(--gold)')}">${rate}%</div>
                <div style="font-size:0.8rem; color:var(--text-muted)">Win Rate</div>
            </div>
            <div style="text-align:center;">
                <div style="font-size:1.5rem">${stats.wins} - ${stats.losses} - ${stats.draws}</div>
                <div style="font-size:0.8rem; color:var(--text-muted)">W - L - D</div>
            </div>
            <div style="text-align:center;">
                <div style="font-size:1.5rem">${stats.totalGames}</div>
                <div style="font-size:0.8rem; color:var(--text-muted)">Total Games</div>
            </div>
        </div>
    `;
    document.getElementById('winRateStats').innerHTML = html;
}

async function submitGameLog(event) {
    event.preventDefault();
    const result = document.getElementById('logResult').value;
    const opponent = document.getElementById('logOpponent').value;
    const notes = document.getElementById('logNotes').value;
    
    if (!currentDetailDeck || !currentDetailDeck.id) return;

    const payload = {
        deckId: currentDetailDeck.id,
        result: result,
        opponentArchetype: opponent,
        notes: notes
    };

    try {
        const res = await fetch('/api/gamelogs', {
            method: 'POST',
            headers: _forgeAuthHeaders(true),
            body: JSON.stringify(payload)
        });
        if (res.ok) {
            document.getElementById('logOpponent').value = '';
            document.getElementById('logNotes').value = '';
            fetchWinRates(currentDetailDeck.id);
            fetchGameLogs(currentDetailDeck.id);
            showToast('Game log added!', false);
        } else {
            showToast('Failed to add game log', true);
        }
    } catch(e) {
        showToast('Error saving log', true);
    }
}

async function fetchGameLogs(deckId) {
    try {
        const res = await fetch(`/api/gamelogs/deck/${deckId}`, { headers: _forgeAuthHeaders(false) });
        if (res.ok) {
            const logs = await res.json();
            const formatLoc = d => new Date(d).toLocaleDateString();
            document.getElementById('gameLogsTable').innerHTML = logs.map(l => `
                <div style="display:flex; justify-content:space-between; border-bottom:1px solid var(--border); padding:0.5rem 0;">
                    <div style="width:60px; font-weight:bold; color:${l.result==='win'?'var(--success)':(l.result==='loss'?'var(--danger)':'var(--gold)')}">${l.result.toUpperCase()}</div>
                    <div style="flex:1">${l.opponentArchetype || 'Unknown'}</div>
                    <div style="flex:2; color:var(--text-muted); font-size:0.9rem">${l.notes||''}</div>
                    <div style="width:80px; text-align:right; font-size:0.8rem; color:var(--text-muted)">${formatLoc(l.date)}</div>
                </div>
            `).join('');
            if(logs.length === 0) document.getElementById('gameLogsTable').innerHTML = "No logs.";
        }
    } catch(e) {}
}
