namespace QHHDesktopStorageBox.Core.Logging;

public interface IAppLogger
{
    void Info(string message);

    void Error(Exception exception, string message);
}

