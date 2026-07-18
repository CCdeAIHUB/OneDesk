namespace OneDesk.Desktop.Services;

public sealed record FileInstallOperation(string Source, string Destination, bool IsDirectory)
{
    public static FileInstallOperation Directory(string source, string destination) => new(source, destination, true);
    public static FileInstallOperation File(string source, string destination) => new(source, destination, false);
}

/// <summary>
/// 以移动目录/文件的方式提交包内容；任何一步失败都会按逆序恢复全部旧目标。
/// 所有路径必须位于同一数据卷，OneDesk 用户数据目录天然满足该不变量。
/// </summary>
public static class FileInstallTransaction
{
    public static FileInstallSession Begin(IReadOnlyList<FileInstallOperation> operations, string backupRoot)
    {
        Directory.CreateDirectory(backupRoot);
        var applied = new List<AppliedOperation>();
        try
        {
            for (var index = 0; index < operations.Count; index++)
            {
                var operation = operations[index];
                EnsureSourceExists(operation);
                Directory.CreateDirectory(Path.GetDirectoryName(operation.Destination) ?? throw new InvalidDataException("InstallDestinationParentMissing"));
                var backup = Path.Combine(backupRoot, $"{index:D4}");
                var hadDestination = operation.IsDirectory
                    ? Directory.Exists(operation.Destination)
                    : System.IO.File.Exists(operation.Destination);
                if (hadDestination) Move(operation.Destination, backup, operation.IsDirectory);
                applied.Add(new AppliedOperation(operation, backup, hadDestination));
                Move(operation.Source, operation.Destination, operation.IsDirectory);
            }

            return new FileInstallSession(applied, backupRoot);
        }
        catch
        {
            Rollback(applied);
            throw;
        }
    }

    public static void Commit(IReadOnlyList<FileInstallOperation> operations, string backupRoot)
    {
        using var transaction = Begin(operations, backupRoot);
        transaction.Complete();
    }

    private static void Rollback(IReadOnlyList<AppliedOperation> applied)
    {
        List<Exception>? errors = null;
        for (var index = applied.Count - 1; index >= 0; index--)
        {
            var entry = applied[index];
            try
            {
                Delete(entry.Operation.Destination, entry.Operation.IsDirectory);
                if (entry.HadDestination) Move(entry.Backup, entry.Operation.Destination, entry.Operation.IsDirectory);
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }
        if (errors is not null) throw new AggregateException("PackageRollbackFailed", errors);
    }

    private static void EnsureSourceExists(FileInstallOperation operation)
    {
        var exists = operation.IsDirectory ? Directory.Exists(operation.Source) : System.IO.File.Exists(operation.Source);
        if (!exists) throw new FileNotFoundException("InstallSourceMissing", operation.Source);
    }

    private static void Move(string source, string destination, bool directory)
    {
        if (directory) Directory.Move(source, destination);
        else System.IO.File.Move(source, destination);
    }

    private static void Delete(string path, bool directory)
    {
        if (directory)
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        else if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
    }

    internal sealed record AppliedOperation(FileInstallOperation Operation, string Backup, bool HadDestination);

    public sealed class FileInstallSession : IDisposable
    {
        private readonly IReadOnlyList<AppliedOperation> _applied;
        private readonly string _backupRoot;
        private int _state;

        internal FileInstallSession(IReadOnlyList<AppliedOperation> applied, string backupRoot)
        {
            _applied = applied;
            _backupRoot = backupRoot;
        }

        /// <summary>
        /// 只有文件之外的校验（例如插件握手）也成功后才能提交；提交前释放会自动恢复旧目标。
        /// </summary>
        public void Complete()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            {
                throw new InvalidOperationException("InstallTransactionAlreadyFinished");
            }

            if (Directory.Exists(_backupRoot)) Directory.Delete(_backupRoot, recursive: true);
        }

        public void Rollback()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 0) != 0) return;
            FileInstallTransaction.Rollback(_applied);
            if (Directory.Exists(_backupRoot)) Directory.Delete(_backupRoot, recursive: true);
        }

        public void Dispose() => Rollback();
    }
}
