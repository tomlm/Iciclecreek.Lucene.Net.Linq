using System;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;

namespace Lucene.Net.Linq.Translation.ResultOperatorHandlers
{
    internal class TakeResultOperationHandler : ResultOperatorHandler<TakeResultOperator>
    {
        protected override void AcceptInternal(TakeResultOperator take, LuceneQueryModel model)
        {
            model.MaxResults = Math.Min(take.GetConstantCount(), model.MaxResults);
        }
    }

    internal class SkipResultOperatorHandler : ResultOperatorHandler<SkipResultOperator>
    {
        protected override void AcceptInternal(SkipResultOperator skip, LuceneQueryModel model)
        {
            var additionalSkip = skip.GetConstantCount();
            model.SkipResults += additionalSkip;

            if (model.MaxResults != int.MaxValue)
            {
                model.MaxResults -= additionalSkip;
            }
        }
    }

    internal class FirstResultOperatorHandler : ResultOperatorHandler<FirstResultOperator>
    {
        protected override void AcceptInternal(FirstResultOperator resultOperator, LuceneQueryModel model)
        {
            // Not simply 1: paging ahead of First may already have left no rows
            // (Take(0), or Take(2).Skip(3), which leaves MaxResults below zero).
            model.MaxResults = Math.Min(model.MaxResults, 1);
        }
    }

    internal class LastResultOperatorHandler : ResultOperatorHandler<LastResultOperator>
    {
        protected override void AcceptInternal(LastResultOperator resultOperator, LuceneQueryModel model)
        {
            model.Last = true;
        }
    }

    internal class MaxResultOperatorHandler : ResultOperatorHandler<MaxResultOperator>
    {
        protected override void AcceptInternal(MaxResultOperator resultOperator, LuceneQueryModel model)
        {
            model.ResetSorts();
            model.AddSort(model.SelectClause, OrderingDirection.Desc);
            model.MaxResults = 1;
        }
    }

    internal class MinResultOperatorHandler : ResultOperatorHandler<MinResultOperator>
    {
        protected override void AcceptInternal(MinResultOperator resultOperator, LuceneQueryModel model)
        {
            model.ResetSorts();
            model.AddSort(model.SelectClause, OrderingDirection.Asc);
            model.MaxResults = 1;
            model.Aggregate = true;
        }
    }

}