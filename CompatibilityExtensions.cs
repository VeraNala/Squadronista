using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Squadronista
{
    internal static class CompatibilityExtensions
    {
        // DistinctBy extension method (for older .NET versions compatibility)
        public static IEnumerable<TSource> DistinctBy<TSource, TKey>(
            this IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector)
        {
            var seenKeys = new HashSet<TKey>();
            foreach (var element in source)
            {
                if (seenKeys.Add(keySelector(element)))
                {
                    yield return element;
                }
            }
        }

        // Order extension method
        public static IOrderedEnumerable<T> Order<T>(this IEnumerable<T> source)
        {
            return source.OrderBy(x => x);
        }

        // GetValueOrDefault for Dictionary
        public static TValue? GetValueOrDefault<TKey, TValue>(
            this Dictionary<TKey, TValue> dictionary,
            TKey key,
            TValue? defaultValue = default)
            where TKey : notnull
        {
            return dictionary.TryGetValue(key, out var value) ? value : defaultValue;
        }

        // IsCompletedSuccessfully for Task
        public static bool IsCompletedSuccessfully(this Task task)
        {
            return task.IsCompleted && !task.IsFaulted && !task.IsCanceled;
        }
    }
}