#nullable enable
using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AutodeskAIBridge.AutoCAD;

public sealed class AutoCadTransactionResult<T>
{
    public bool Success { get; internal set; }
    public T? Value { get; internal set; }
    public string ErrorCode { get; internal set; } = string.Empty;
    public string ErrorMessage { get; internal set; } = string.Empty;
    public List<string> Warnings { get; } = new();
    public List<string> CreatedHandles { get; } = new();
    public List<string> ModifiedHandles { get; } = new();
    public List<string> DeletedHandles { get; } = new();
}

/// <summary>Reusable AutoCAD document-lock and transaction boundary. Failed actions roll back.</summary>
public sealed class AutoCadTransactionRunner
{
    public AutoCadTransactionResult<T> Run<T>(Document document, Func<AutoCadTransactionContext, T> action)
    {
        var result = new AutoCadTransactionResult<T>();
        try
        {
            using var documentLock = document.LockDocument();
            using var transaction = document.Database.TransactionManager.StartTransaction();
            var context = new AutoCadTransactionContext(transaction, result.CreatedHandles.Add, result.ModifiedHandles.Add, result.DeletedHandles.Add);
            result.Value = action(context);
            transaction.Commit();
            result.Success = true;
            return result;
        }
        catch (Exception exception)
        {
            result.CreatedHandles.Clear(); result.ModifiedHandles.Clear(); result.DeletedHandles.Clear();
            result.ErrorCode = exception is AutoCadOperationException operationException
                ? operationException.Code
                : exception.Message.IndexOf("lock", StringComparison.OrdinalIgnoreCase) >= 0 ? "DOCUMENT_LOCK_FAILED" : "TRANSACTION_FAILED";
            result.ErrorMessage = exception.Message;
            return result;
        }
    }
}

public sealed class AutoCadTransactionContext
{
    private readonly Transaction _transaction;
    private readonly Action<string> _created;
    private readonly Action<string> _modified;
    private readonly Action<string> _deleted;
    internal AutoCadTransactionContext(Transaction transaction, Action<string> created, Action<string> modified, Action<string> deleted) { _transaction = transaction; _created = created; _modified = modified; _deleted = deleted; }
    public Transaction Transaction => _transaction;
    public void TrackCreated(ObjectId id) => _created(id.Handle.ToString());
    public void TrackModified(ObjectId id) => _modified(id.Handle.ToString());
    public void TrackDeleted(ObjectId id) => _deleted(id.Handle.ToString());
}
