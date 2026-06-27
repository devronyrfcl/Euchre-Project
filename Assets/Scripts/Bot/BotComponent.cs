// ─── BotComponent.cs ───────────────────────────────────────────
// Attach to the same GameObject as GameManagerComponent.
// Handles all bot decision-making for seats NOT occupied by the
// human player.  The host runs all bot logic and broadcasts via
// existing Photon RPCs — non-host clients never call bot methods.
// ───────────────────────────────────────────────────────────────
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BotComponent : MonoBehaviour
{
    // ── Which seats are bots ────────────────────────────────────
    private HashSet<int> botSeats = new HashSet<int>();

    // ── Difficulty knobs (tweak in Inspector) ───────────────────
    [Header("Bot Behaviour")]
    [Tooltip("Seconds the bot 'thinks' before acting (feels more natural)")]
    [Range(0.3f, 2.5f)]
    public float thinkDelay = 0.8f;

    // ───────────────────────────────────────────────────────────
    // PUBLIC API called by GameManagerComponent
    // ───────────────────────────────────────────────────────────

    /// <summary>Register which seats are controlled by bots.</summary>
    public void SetBotSeats(List<int> seats)
    {
        botSeats.Clear();
        foreach (int s in seats) botSeats.Add(s);
        Debug.Log($"[Bot] Bot seats: {string.Join(",", botSeats)}");
    }

    /// <summary>Returns true if the given seat is a bot.</summary>
    public bool IsBotTurn(int seat) => botSeats.Contains(seat);

    // ── Trump Phase ─────────────────────────────────────────────

    /// <summary>
    /// Called during trump selection.
    /// <para>cycled == 0 → round 1 (order up or pass the upcard)</para>
    /// <para>cycled == 1 → round 2 (name a suit or pass; dealer is forced)</para>
    /// Calls <paramref name="onPick"/> with a suit string, or
    /// <paramref name="onPass"/> with no arguments.
    /// </summary>
    public void BotDecideTrump(
        int    seat,
        char   upcardSuit,
        int    cycled,
        int    dealer,
        Action<string> onPick,
        Action         onPass)
    {
        StartCoroutine(BotTrumpRoutine(seat, upcardSuit, cycled, dealer, onPick, onPass));
    }

    // ── Discard Phase (dealer only after picking up) ────────────

    /// <summary>
    /// Bot dealer selects one card to discard after picking up the upcard.
    /// Calls <paramref name="onDiscard"/> with the card string to remove.
    /// </summary>
    public void BotDiscard(
        List<string>              hand,
        char                      trump,
        Dictionary<char, char>    alternateSuits,
        Action<string>            onDiscard)
    {
        StartCoroutine(BotDiscardRoutine(hand, trump, alternateSuits, onDiscard));
    }

    // ── Card Play ───────────────────────────────────────────────

    /// <summary>
    /// Bot chooses a card to play this trick.
    /// Calls <paramref name="onPlay"/> with the chosen card string.
    /// </summary>
    public void BotPlayCard(
        int                    seat,
        List<string>           hand,
        List<string>           playedCards,
        char                   trump,
        Dictionary<char, char> alternateSuits,
        Action<string>         onPlay)
    {
        StartCoroutine(BotPlayRoutine(seat, hand, playedCards, trump, alternateSuits, onPlay));
    }

    // ───────────────────────────────────────────────────────────
    // COROUTINES
    // ───────────────────────────────────────────────────────────

    IEnumerator BotTrumpRoutine(
        int    seat,
        char   upcardSuit,
        int    cycled,
        int    dealer,
        Action<string> onPick,
        Action         onPass)
    {
        yield return new WaitForSeconds(thinkDelay);

        // Retrieve this bot's hand from GameManagerComponent
        GameManagerComponent gm = GetComponent<GameManagerComponent>();
        List<string> hand = GetBotHand(seat);

        if (cycled == 0)
        {
            // Round 1 — order up upcard suit if bot has ≥ 2 trump cards
            int trumpCount = CountTrumpCards(hand, upcardSuit, GetAlternateSuit(upcardSuit));
            if (trumpCount >= 2)
                onPick?.Invoke(upcardSuit.ToString());
            else
                onPass?.Invoke();
        }
        else
        {
            // Round 2 — bot names its strongest suit (excluding upcard suit)
            bool forcedPick = (seat == dealer);
            string bestSuit = BestSuitToName(hand, upcardSuit);

            if (bestSuit != null)
                onPick?.Invoke(bestSuit);
            else if (forcedPick)
                onPick?.Invoke(FallbackSuit(upcardSuit)); // must pick something
            else
                onPass?.Invoke();
        }
    }

    IEnumerator BotDiscardRoutine(
        List<string>           hand,
        char                   trump,
        Dictionary<char, char> alternateSuits,
        Action<string>         onDiscard)
    {
        yield return new WaitForSeconds(thinkDelay);

        // Discard the lowest-value non-trump card; if all trump, discard lowest trump
        string worst = WorstCard(hand, trump, alternateSuits);
        onDiscard?.Invoke(worst);
    }

    IEnumerator BotPlayRoutine(
        int                    seat,
        List<string>           hand,
        List<string>           playedCards,
        char                   trump,
        Dictionary<char, char> alternateSuits,
        Action<string>         onPlay)
    {
        yield return new WaitForSeconds(thinkDelay);

        if (hand == null || hand.Count == 0)
        {
            Debug.LogWarning($"[Bot] Seat {seat} has empty hand!");
            onPlay?.Invoke(null);
            yield break;
        }

        string chosen = ChooseCardToPlay(hand, playedCards, trump, alternateSuits);
        onPlay?.Invoke(chosen);
    }

    // ───────────────────────────────────────────────────────────
    // DECISION LOGIC
    // ───────────────────────────────────────────────────────────

    // ── Trump selection helpers ─────────────────────────────────

    int CountTrumpCards(List<string> hand, char trumpSuit, char altSuit)
    {
        int count = 0;
        foreach (string c in hand)
        {
            if (c[1] == trumpSuit) count++;                      // any trump suit card
            else if (c[0] == 'J' && c[1] == altSuit) count++;   // left bower
        }
        return count;
    }

    /// <summary>Pick the best suit to name in round 2 (must differ from excluded).</summary>
    string BestSuitToName(List<string> hand, char excludedSuit)
    {
        char[] suits = { 'S', 'H', 'C', 'D' };
        int bestCount = 1; // need at least 2 trump to bother picking
        string bestSuit = null;

        foreach (char s in suits)
        {
            if (s == excludedSuit) continue;
            char alt = GetAlternateSuit(s);
            int cnt = CountTrumpCards(hand, s, alt);
            if (cnt > bestCount)
            {
                bestCount = cnt;
                bestSuit  = s.ToString();
            }
        }
        return bestSuit; // null = pass
    }

    string FallbackSuit(char excludedSuit)
    {
        char[] suits = { 'S', 'H', 'C', 'D' };
        foreach (char s in suits)
            if (s != excludedSuit) return s.ToString();
        return "S";
    }

    char GetAlternateSuit(char trump)
    {
        return trump switch { 'S' => 'C', 'C' => 'S', 'H' => 'D', 'D' => 'H', _ => 'S' };
    }

    // ── Discard helper ──────────────────────────────────────────

    string WorstCard(List<string> hand, char trump, Dictionary<char, char> alternateSuits)
    {
        string worst     = hand[0];
        int    worstVal  = 99;

        foreach (string c in hand)
        {
            int val = CardKeepValue(c, trump, alternateSuits);
            if (val < worstVal)
            {
                worstVal = val;
                worst    = c;
            }
        }
        return worst;
    }

    /// <summary>
    /// Higher = more valuable to keep.
    /// Trump cards (including bowers) get highest scores.
    /// </summary>
    int CardKeepValue(string card, char trump, Dictionary<char, char> alternateSuits)
    {
        char val  = card[0];
        char suit = card[1];
        char alt  = alternateSuits.ContainsKey(trump) ? alternateSuits[trump] : GetAlternateSuit(trump);

        bool isRight = val == 'J' && suit == trump;
        bool isLeft  = val == 'J' && suit == alt;

        if (isRight) return 100;
        if (isLeft)  return 99;

        int faceVal = val switch { 'A' => 7, 'K' => 6, 'Q' => 5, 'J' => 4, 'T' => 3, '9' => 2, _ => 1 };

        if (suit == trump) return 50 + faceVal;  // trump non-bower
        return faceVal;                           // off-suit
    }

    // ── Card play logic ─────────────────────────────────────────

    string ChooseCardToPlay(
        List<string>           hand,
        List<string>           playedCards,
        char                   trump,
        Dictionary<char, char> alternateSuits)
    {
        char alt = alternateSuits.ContainsKey(trump)
            ? alternateSuits[trump]
            : GetAlternateSuit(trump);

        // Determine led suit
        char leadSuit = 'N';
        if (playedCards.Count > 0)
        {
            string lead = playedCards[0];
            bool leadIsLeft = lead[0] == 'J' && lead[1] == alt;
            leadSuit = leadIsLeft ? trump : lead[1];
        }

        // Separate legal cards (must follow suit if possible)
        List<string> legal = GetLegalCards(hand, leadSuit, trump, alt);

        if (legal.Count == 0) legal = new List<string>(hand); // safety

        // If leading (no cards played yet) — play highest trump or highest card
        if (playedCards.Count == 0)
            return BestLeadCard(legal, trump, alt);

        // Determine current winning card
        string winCard  = DetermineWinner(playedCards, leadSuit, trump, alt);
        int    winValue = EvalCard(winCard, leadSuit, trump, alt);

        // Try to win the trick with the lowest winning card
        string winner = LowestWinner(legal, winValue, leadSuit, trump, alt);
        if (winner != null) return winner;

        // Can't win — throw lowest value card
        return LowestCard(legal, trump, alt);
    }

    List<string> GetLegalCards(List<string> hand, char leadSuit, char trump, char alt)
    {
        if (leadSuit == 'N') return new List<string>(hand);

        List<string> followers = new List<string>();
        foreach (string c in hand)
        {
            bool isLeft = c[0] == 'J' && c[1] == alt;
            bool follows = isLeft
                ? leadSuit == trump
                : c[1] == leadSuit;
            if (follows) followers.Add(c);
        }
        return followers.Count > 0 ? followers : new List<string>(hand);
    }

    string BestLeadCard(List<string> legal, char trump, char alt)
    {
        // Lead highest trump, else highest off-suit ace/king
        string best   = legal[0];
        int    bestV  = -1;

        foreach (string c in legal)
        {
            // Weight trump heavily so bot leads trump aggressively
            int v = EvalCard(c, trump, trump, alt); // treat lead=trump so all trump rank high
            if (v > bestV) { bestV = v; best = c; }
        }
        return best;
    }

    string LowestWinner(List<string> legal, int mustBeat, char leadSuit, char trump, char alt)
    {
        string best  = null;
        int    bestV = int.MaxValue;

        foreach (string c in legal)
        {
            int v = EvalCard(c, leadSuit, trump, alt);
            if (v > mustBeat && v < bestV)
            {
                bestV = v;
                best  = c;
            }
        }
        return best;
    }

    string LowestCard(List<string> legal, char trump, char alt)
    {
        string worst  = legal[0];
        int    worstV = int.MaxValue;

        foreach (string c in legal)
        {
            int v = CardKeepValue(c, trump,
                new Dictionary<char, char> { { trump, alt }, { alt, trump } });
            if (v < worstV) { worstV = v; worst = c; }
        }
        return worst;
    }

    string DetermineWinner(List<string> played, char leadSuit, char trump, char alt)
    {
        string best  = played[0];
        int    bestV = EvalCard(played[0], leadSuit, trump, alt);

        foreach (string c in played)
        {
            int v = EvalCard(c, leadSuit, trump, alt);
            if (v > bestV) { bestV = v; best = c; }
        }
        return best;
    }

    /// <summary>
    /// Mirror of GameManagerComponent.EvaluateCard — kept local to avoid coupling.
    /// </summary>
    int EvalCard(string card, char leadSuit, char trump, char alt)
    {
        char val  = card[0];
        char suit = card[1];

        bool isRight = val == 'J' && suit == trump;
        bool isLeft  = val == 'J' && suit == alt;

        if (isRight) return 15;
        if (isLeft)  return 14;

        if (suit == trump)
            return val switch { 'A'=>13,'K'=>12,'Q'=>11,'T'=>10,'9'=>9, _=>0 };

        if (suit == leadSuit)
            return val switch { 'A'=>7,'K'=>6,'Q'=>5,'J'=>4,'T'=>3,'9'=>2, _=>0 };

        return 0; // off-suit, can't win
    }

    // ───────────────────────────────────────────────────────────
    // HAND ACCESS
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the bot's current hand from GameManagerComponent via reflection-free
    /// helper.  GameManagerComponent exposes GetBotHand(seat) — add that method
    /// as shown in the integration notes below.
    /// </summary>
    List<string> GetBotHand(int seat)
    {
        GameManagerComponent gm = GetComponent<GameManagerComponent>();
        if (gm == null) return new List<string>();
        return gm.GetPlayerHand(seat);
    }
}