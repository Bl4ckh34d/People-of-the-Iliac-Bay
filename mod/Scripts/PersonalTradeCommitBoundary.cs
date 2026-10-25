using System;
using System.Security.Cryptography;
using System.Text;

namespace PeopleOfTheIliacBay.DFU
{
    internal sealed class PersonalTradeCommitOutcomeUnknownException : InvalidOperationException
    {
        internal PersonalTradeCommitOutcomeUnknownException(Exception cause)
            : base("Personal trade store commit threw; outcome unknown. Native rollback is withheld.", cause)
        {
        }
    }

    internal static class PersonalTradeCommitBoundary
    {
        // Bounded, stable identities tied to the source transaction. The two
        // domains cannot reuse a receipt as an effect on retry or save reload.
        internal static string SourceBoundId(string domain, string transactionId)
        {
            if ((domain != "receipt" && domain != "effect") ||
                string.IsNullOrWhiteSpace(transactionId))
                throw new ArgumentException("Personal trade source identity is missing.");
            using (var sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(transactionId));
                return "personal-trade:" + domain + ":" + BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
            }
        }

        // DFU ItemCollection.RemoveItem removes by UID, not object identity.
        // Compare references before removal so a split-UID collision never
        // deletes an unrelated destination item during rollback.
        internal static void Restore<TItem>(TItem original, TItem moved, int originalCount,
            Func<TItem, ulong> uid, Func<ulong, TItem> sourceItem, Func<ulong, TItem> targetItem,
            Action<TItem> sourceAdd, Action<TItem> sourceRemove, Action<TItem> targetRemove,
            Action<TItem, int> setCount, Action restoreGold, Func<bool> goldMatches)
            where TItem : class
        {
            if (original == null || uid == null || sourceItem == null || targetItem == null ||
                sourceAdd == null || sourceRemove == null || targetRemove == null ||
                setCount == null || restoreGold == null || goldMatches == null)
                throw new ArgumentNullException("Personal trade rollback port is incomplete.");
            Exception itemError = null, goldError = null;
            try
            {
                if (moved != null)
                {
                    if (ReferenceEquals(targetItem(uid(moved)), moved)) targetRemove(moved);
                    if (!ReferenceEquals(moved, original) && ReferenceEquals(sourceItem(uid(moved)), moved))
                        sourceRemove(moved);
                }
                setCount(original, originalCount);
                if (sourceItem(uid(original)) == null) sourceAdd(original);
                if (!ReferenceEquals(sourceItem(uid(original)), original) ||
                    moved != null && ReferenceEquals(targetItem(uid(moved)), moved))
                    throw new InvalidOperationException("Exact source item identity was not restored.");
            }
            catch (Exception error) { itemError = error; }
            try
            {
                restoreGold();
                if (!goldMatches()) throw new InvalidOperationException("Exact player gold was not restored.");
            }
            catch (Exception error) { goldError = error; }
            if (itemError != null && goldError != null)
                throw new AggregateException("Personal trade rollback failed for items and gold.", itemError, goldError);
            if (itemError != null)
                throw new InvalidOperationException("Personal trade item rollback failed.", itemError);
            if (goldError != null)
                throw new InvalidOperationException("Personal trade gold rollback failed.", goldError);
        }

        // Receipt is built and validated from native observations before commit.
        // A rejected commit publishes nothing. An exceptional commit may already
        // have written state, so callers must not roll native objects back.
        internal static bool TryCommit<TReceipt>(Func<bool> commit, TReceipt preparedReceipt,
            out TReceipt receipt) where TReceipt : class
        {
            receipt = null;
            if (commit == null) throw new ArgumentNullException(nameof(commit));
            if (preparedReceipt == null) throw new ArgumentNullException(nameof(preparedReceipt));
            bool accepted;
            try { accepted = commit(); }
            catch (Exception error) { throw new PersonalTradeCommitOutcomeUnknownException(error); }
            if (!accepted) return false;
            receipt = preparedReceipt;
            return true;
        }
    }
}
