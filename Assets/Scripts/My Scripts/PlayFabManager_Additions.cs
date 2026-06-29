// ═══════════════════════════════════════════════════════════════════════
// PLAYFABMANAGER ADDITIONS
// Add these to your existing PlayFabManager.cs
// ═══════════════════════════════════════════════════════════════════════

// ─── 1. Add these private fields near your other cached fields ──────────

/*
private string _cachedAvatarUrl = "";
private int    _cachedPC        = 0;
private string _cachedDisplayName = "";
*/

// ─── 2. Add these public read-only properties near your other accessors ─

/*
public string CachedPlayFabId    => _playFabId;
public string CachedDisplayName  => _cachedDisplayName;
public string CachedAvatarUrl    => _cachedAvatarUrl;
public int    CachedPokerChips   => _cachedPC;
*/

// ─── 3. In FetchCurrencies() success callback, add: ────────────────────

/*
    result =>
    {
        int gp = result.VirtualCurrency.TryGetValue("GP", out int gpVal) ? gpVal : 0;
        int pc = result.VirtualCurrency.TryGetValue("PC", out int pcVal) ? pcVal : 0;

        _cachedPC = pc;   // ← ADD THIS

        SetTextArray(gpTexts, gp.ToString());
        SetTextArray(pcTexts, pc.ToString());
    },
*/

// ─── 4. In FetchProfileAndStats() success callback, add: ───────────────

/*
    profileResult =>
    {
        string displayName = profileResult.PlayerProfile?.DisplayName;
        if (!string.IsNullOrEmpty(displayName))
        {
            _cachedDisplayName = displayName;   // ← ADD THIS
            SetTextArray(displayNameTexts, displayName);
        }
        ...
    }
*/

// ─── 5. In SaveImageUrlToPlayFab(), inside the success callback, add: ──

/*
    result =>
    {
        _cachedAvatarUrl = url;   // ← ADD THIS (url is the parameter passed in)
        Debug.Log("[PlayFabManager] Avatar URL saved.");
        FetchProfileImage();
    },
*/

// ─── 6. In FetchProfileImage() success callback, add: ──────────────────

/*
    result =>
    {
        string avatarUrl = result.PlayerProfile?.AvatarUrl;
        if (!string.IsNullOrEmpty(avatarUrl))
        {
            _cachedAvatarUrl = avatarUrl;   // ← ADD THIS
            StartCoroutine(LoadImage(avatarUrl));
        }
        ...
    }
*/

// ═══════════════════════════════════════════════════════════════════════
// That's all. PlayFabManager now exposes 4 read-only properties
// that MatchConfigBuilder reads when building the match config JSON.
// ═══════════════════════════════════════════════════════════════════════
