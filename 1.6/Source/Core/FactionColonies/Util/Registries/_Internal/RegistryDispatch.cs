using System;
using System.Collections.Generic;

namespace FactionColonies
{
    /// <summary>
    /// Internal helpers for the dispatch idioms every per-domain registry uses: iterate,
    /// short-circuit, aggregate, find. Each helper wraps the participant invocation in
    /// a try/catch that logs via <see cref="LogUtil.Error"/> with a consistent format:
    /// <c>"&lt;interface&gt; &lt;impl&gt; threw in &lt;call&gt;: &lt;exception&gt;"</c>.
    /// </summary>
    internal static class RegistryDispatch
    {
        /// <summary>Calls <paramref name="action"/> on every item; exceptions are caught and logged.</summary>
        public static void Each<T>(IReadOnlyList<T> items, Action<T> action, string call) where T : class
        {
            foreach (T item in items)
            {
                try { action(item); }
                catch (Exception e) { Log(item, call, e); }
            }
        }

        /// <summary>
        /// Like <see cref="Each{T}"/> but invokes <paramref name="invalidate"/> after every item.
        /// Used by registries (Lifecycle settlement events, TaxTick per-settlement) that need to
        /// re-invalidate a cache between participants so later participants see fresh state.
        /// </summary>
        public static void EachInvalidating<T>(IReadOnlyList<T> items, Action<T> action, Action invalidate, string call) where T : class
        {
            foreach (T item in items)
            {
                try { action(item); }
                catch (Exception e) { Log(item, call, e); }
                invalidate();
            }
        }

        /// <summary>Short-circuits: returns false on the first item whose predicate is false.</summary>
        public static bool All<T>(IReadOnlyList<T> items, Func<T, bool> predicate, string call) where T : class
        {
            foreach (T item in items)
            {
                try { if (!predicate(item)) return false; }
                catch (Exception e) { Log(item, call, e); }
            }
            return true;
        }

        /// <summary>Folds with <paramref name="reducer"/> starting from <paramref name="seed"/>.</summary>
        public static TAcc Aggregate<T, TAcc>(IReadOnlyList<T> items, TAcc seed, Func<TAcc, T, TAcc> reducer, string call) where T : class
        {
            TAcc acc = seed;
            foreach (T item in items)
            {
                try { acc = reducer(acc, item); }
                catch (Exception e) { Log(item, call, e); }
            }
            return acc;
        }

        /// <summary>Returns the first item matching <paramref name="predicate"/>, or null.</summary>
        public static T First<T>(IReadOnlyList<T> items, Func<T, bool> predicate, string call) where T : class
        {
            foreach (T item in items)
            {
                try { if (predicate(item)) return item; }
                catch (Exception e) { Log(item, call, e); }
            }
            return null;
        }

        /// <summary>Returns the first item where <paramref name="selector"/> yields non-null.</summary>
        public static T FirstNonNull<T, TResult>(IReadOnlyList<T> items, Func<T, TResult> selector, string call)
            where T : class
            where TResult : class
        {
            foreach (T item in items)
            {
                try { if (selector(item) is object) return item; }
                catch (Exception e) { Log(item, call, e); }
            }
            return null;
        }

        private static void Log<T>(T offender, string call, Exception e) where T : class
        {
            LogUtil.Error($"{typeof(T).Name} {offender?.GetType().Name} threw in {call}: {e}");
        }
    }
}
