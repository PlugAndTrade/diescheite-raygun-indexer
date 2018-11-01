using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PlugAndTrade.DieScheite.RayGun.Service
{
    public static class EnumerableExtensions
    {
        public static T MaxBy<T, M>(this IEnumerable<T> source, Func<T, M> selector) where M : IComparable
        {
            return source.Aggregate((record, next) =>
                Comparer<M>.Default.Compare(selector(next), selector(record)) > 0
                    ? next
                    : record);
        }
    }
}
