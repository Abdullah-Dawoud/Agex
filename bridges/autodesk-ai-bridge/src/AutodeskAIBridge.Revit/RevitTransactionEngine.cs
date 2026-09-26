#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace AutodeskAIBridge.Revit;

public sealed record RevitBatchStep(string OperationName, Func<RevitTransactionContext, object?> Execute);
public sealed record RevitDryRunPlan(string OperationName, IReadOnlyList<string> Steps, IReadOnlyList<string> Warnings);

/// <summary>Atomic transaction-group runner. Any failed step rolls back entire group.</summary>
public sealed class RevitAtomicBatchRunner
{
    private readonly RevitTransactionRunner _transactions = new();

    public RevitDryRunPlan Plan(string operationName, IReadOnlyList<RevitBatchStep> steps)
        => new(operationName, steps.Select(step => step.OperationName).ToArray(), Array.Empty<string>());

    public RevitTransactionResult<IReadOnlyList<object?>> Run(Document document, string name, IReadOnlyList<RevitBatchStep> steps, bool dryRun = false)
    {
        var result = new RevitTransactionResult<IReadOnlyList<object?>>();
        if (dryRun)
        {
            result.Success = true;
            result.Value = new List<object?>();
            return result;
        }
        using var group = new TransactionGroup(document, name);
        try
        {
            if (group.Start() != TransactionStatus.Started)
            {
                result.ErrorCode = "TRANSACTION_FAILED";
                result.ErrorMessage = "Transaction group could not start.";
                return result;
            }
            var values = new List<object?>();
            foreach (var step in steps)
            {
                var stepResult = _transactions.Run(document, step.OperationName, context => step.Execute(context));
                result.Warnings.AddRange(stepResult.Warnings);
                result.ChangedElementIds.AddRange(stepResult.ChangedElementIds);
                result.CreatedElementIds.AddRange(stepResult.CreatedElementIds);
                result.ModifiedElementIds.AddRange(stepResult.ModifiedElementIds);
                result.DeletedElementIds.AddRange(stepResult.DeletedElementIds);
                if (!stepResult.Success)
                {
                    group.RollBack();
                    result.ChangedElementIds.Clear(); result.CreatedElementIds.Clear(); result.ModifiedElementIds.Clear(); result.DeletedElementIds.Clear();
                    result.ErrorCode = stepResult.ErrorCode;
                    result.ErrorMessage = stepResult.ErrorMessage;
                    return result;
                }
                values.Add(stepResult.Value);
            }
            if (group.Assimilate() != TransactionStatus.Committed)
            {
                result.ErrorCode = "TRANSACTION_FAILED";
                result.ErrorMessage = "Transaction group could not commit.";
                return result;
            }
            result.Value = values;
            result.Success = true;
            return result;
        }
        catch (Exception exception)
        {
            if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
            result.ErrorCode = "TRANSACTION_FAILED";
            result.ErrorMessage = exception.Message;
            return result;
        }
    }
}

/// <summary>SubTransaction wrapper for operations that need nested rollback inside an outer transaction.</summary>
public sealed class RevitSubTransactionScope : IDisposable
{
    private readonly SubTransaction _transaction;
    private bool _completed;
    public RevitSubTransactionScope(Document document) { _transaction = new SubTransaction(document); _transaction.Start(); }
    public void Commit() { if (!_completed) { _transaction.Commit(); _completed = true; } }
    public void RollBack() { if (!_completed) { _transaction.RollBack(); _completed = true; } }
    public void Dispose() { if (!_completed) RollBack(); _transaction.Dispose(); }
}
