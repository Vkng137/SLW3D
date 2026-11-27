using System.Diagnostics;
using System.Security;
using System.Security.Principal;

namespace Desktop3D.Controller;

/// <summary>"Start at login": a per-user Task Scheduler task that starts the controller 10 s after
/// logon, with normal (not admin) rights and normal priority. Normal rights on purpose: apps opened
/// from the desktop inherit them, and must not run as administrator. Normal priority on purpose:
/// the Task Scheduler default (below normal) would be inherited by games launched from the tiles.</summary>
static class Autostart
{
    public const string TaskName = "SLW3D";

    public static bool IsOn() => Run("/Query /TN \"" + TaskName + "\"", out _) == 0;

    public static void Enable(string controllerExe)
    {
        string user = WindowsIdentity.GetCurrent().Name;
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts the SLW3D controller at login (project: {SecurityElement.Escape(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(controllerExe))))}).</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{SecurityElement.Escape(user)}</UserId><Delay>PT10S</Delay></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{SecurityElement.Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>{SecurityElement.Escape(controllerExe)}</Command><WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(controllerExe))}</WorkingDirectory></Exec></Actions>
            </Task>
            """;
        string tmp = Path.Combine(Path.GetTempPath(), "Desktop3D-autostart.xml");
        File.WriteAllText(tmp, xml, System.Text.Encoding.Unicode);
        try
        {
            if (Run($"/Create /TN \"{TaskName}\" /XML \"{tmp}\" /F", out string output) != 0)
                throw new InvalidOperationException(output.Trim());
            Log.Write("Start at login: ON (Task Scheduler task created)");
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    public static void Disable()
    {
        if (Run($"/Delete /TN \"{TaskName}\" /F", out string output) != 0)
            throw new InvalidOperationException(output.Trim());
        Log.Write("Start at login: OFF (Task Scheduler task deleted)");
    }

    static int Run(string args, out string output)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(15000))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("schtasks.exe did not finish within 15 s");
        }
        Task.WaitAll(new Task[] { outTask, errTask }, 2000);
        output = (outTask.IsCompletedSuccessfully ? outTask.Result : "")
               + (errTask.IsCompletedSuccessfully ? errTask.Result : "");
        return p.ExitCode;
    }
}
