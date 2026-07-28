using System;

/// <summary>
/// Common contract for trump-selection popups so GameplayNetworkManager can pick between
/// the standard TrumpSelectionUI (has go-alone) and TrumpSelectionUIForRatedGame
/// (no go-alone) without branching at every call site.
/// </summary>
public interface ITrumpSelectionUI
{
    /// <summary>canOrderUp: false when the player lacks a natural trump card (house rule) —
    /// the dealer is always exempt from this requirement.</summary>
    void ShowPhase1(char upcardSuit, bool canPass, bool canOrderUp, Action onOrderUp, Action onPass);

    /// <summary>canCallSuit: per-suit natural-trump check (house rule) — the dealer is always
    /// exempt, so pass a predicate that always returns true for the dealer.</summary>
    void ShowPhase2(string excludedSuit, bool canPass, Func<char, bool> canCallSuit,
                     Action<char> onSuitPicked, Action onPass);

    void Hide();
}
