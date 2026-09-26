#nullable enable
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace AutodeskAIBridge.Revit;

public sealed class RevitTransactionResult<T>
{
    public bool Success { get; internal set; }
    public T? Value { get; internal set; }
    public string ErrorCode { get; internal set; } = string.Empty;
    public string ErrorMessage { get; internal set; } = string.Empty;
    public List<string> Warnings { get; } = new();
    public List<ElementId> ChangedElementIds { get; } = new();
    public List<ElementId> CreatedElementIds { get; } = new();
    public List<ElementId> ModifiedElementIds { get; } = new();
    public List<ElementId> DeletedElementIds { get; } = new();
}

/// <summary>Owns transaction lifetime and rollback. Caller never receives an open transaction.</summary>
public sealed class RevitTransactionRunner
{
    public RevitTransactionResult<T> Run<T>(Document document, string name, Func<RevitTransactionContext, T> action)
    {
        var result = new RevitTransactionResult<T>();
        using var transaction = new Transaction(document, name);

        try
        {
            var status = transaction.Start();
            if (status != TransactionStatus.Started)
            {
            result.ErrorCode = "TRANSACTION_FAILED";
                result.ErrorMessage = status.ToString();
                return result;
            }

            var failureOptions = transaction.GetFailureHandlingOptions();
            failureOptions.SetFailuresPreprocessor(new RevitFailurePreprocessor(result.Warnings));
            transaction.SetFailureHandlingOptions(failureOptions);

            var context = new RevitTransactionContext(document, result.ChangedElementIds.Add, result.CreatedElementIds.Add, result.ModifiedElementIds.Add, result.DeletedElementIds.Add);
            result.Value = action(context);
            var commitStatus = transaction.Commit();
            if (commitStatus != TransactionStatus.Committed)
            {
                result.ChangedElementIds.Clear(); result.CreatedElementIds.Clear(); result.ModifiedElementIds.Clear(); result.DeletedElementIds.Clear();
                result.ErrorCode = "TRANSACTION_FAILED";
                result.ErrorMessage = commitStatus.ToString();
                return result;
            }

            result.Success = true;
            return result;
        }
        catch (Exception exception)
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
                transaction.RollBack();
            result.ChangedElementIds.Clear(); result.CreatedElementIds.Clear(); result.ModifiedElementIds.Clear(); result.DeletedElementIds.Clear();
            result.ErrorCode = exception is ParameterOperationException parameterException ? parameterException.Code : exception is RevitOperationException operationException ? operationException.Code : "TRANSACTION_FAILED";
            result.ErrorMessage = exception.Message;
            return result;
        }
    }
}

internal sealed class RevitFailurePreprocessor : IFailuresPreprocessor
{
    private readonly ICollection<string> _warnings;

    internal RevitFailurePreprocessor(ICollection<string> warnings) => _warnings = warnings;

    public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
    {
        foreach (var failure in failuresAccessor.GetFailureMessages())
        {
            _warnings.Add(failure.GetDescriptionText());
            if (failure.GetSeverity() == FailureSeverity.Warning)
                failuresAccessor.DeleteWarning(failure);
        }

        return FailureProcessingResult.Continue;
    }
}

public sealed class RevitTransactionContext
{
    private readonly Action<ElementId> _track;
    private readonly Action<ElementId> _trackCreated;
    private readonly Action<ElementId> _trackModified;
    private readonly Action<ElementId> _trackDeleted;

    internal RevitTransactionContext(Document document, Action<ElementId> track, Action<ElementId>? trackCreated = null, Action<ElementId>? trackModified = null, Action<ElementId>? trackDeleted = null)
    {
        Document = document;
        _track = track;
        _trackCreated = trackCreated ?? track;
        _trackModified = trackModified ?? track;
        _trackDeleted = trackDeleted ?? track;
    }

    public Document Document { get; }

    public void Track(ElementId elementId)
    {
        _track(elementId);
    }

    public void TrackCreated(ElementId elementId) => _trackCreated(elementId);
    public void TrackModified(ElementId elementId) => _trackModified(elementId);
    public void TrackDeleted(ElementId elementId) => _trackDeleted(elementId);
}
