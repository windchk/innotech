using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Innotech.Sap.Api.Tests;

public class FakeOrder
{
    public int DocEntry { get; set; }
    public int DocNum { get; set; }
    public bool Cancelled { get; set; }
    public string HeaderValue { get; set; } = "HEADER_OLD";
    public List<FakeLine> Lines { get; set; } = [new() { LineNum = 0 }, new() { LineNum = 1 }, new() { LineNum = 2 }];
}

public class FakeLine
{
    public int LineNum { get; set; }
    public string U_SO_Ref { get; set; } = "OLD_REF";
    public int U_So_Line_Ref { get; set; } = -1;

    public FakeLine Copy() => new()
    {
        LineNum = LineNum,
        U_SO_Ref = U_SO_Ref,
        U_So_Line_Ref = U_So_Line_Ref
    };
}

public class FakeSapCompany
{
    public string CompanyDB { get; set; } = "TEST_COMPANY";
    public string CompanyName { get; set; } = "Test Company";
    public bool Connected { get; set; }
    public int ConnectError { get; set; }
    public Exception ConnectException { get; set; }
    public Dictionary<int, FakeOrder> Orders { get; } = new()
    {
        [1] = new() { DocEntry = 1, DocNum = 1001 },
        [2] = new() { DocEntry = 2, DocNum = 1002 },
        [3] = new() { DocEntry = 3, DocNum = 1003 },
        [4] = new() { DocEntry = 4, DocNum = 1004 }
    };
    public List<string> Events { get; } = [];
    public int FailUpdateEntry { get; set; }
    public bool FailCancel { get; set; }
    public int LastError { get; set; }

    public object GetBusinessObject(int type) => new FakeDocument(this);

    public void Disconnect()
    {
        Events.Add("disconnect");
        Connected = false;
    }

    public void GetLastError(out int code, out string message)
    {
        code = LastError;
        message = code == 0 ? "" : "SAP rejected O'Brien's order";
    }
}

public class FakeDocument(FakeSapCompany company)
{
    private FakeOrder order;
    public int DocNum => order.DocNum;
    public string PendingOldCode { get; set; }
    public List<FakeLine> PendingLines { get; set; }
    public int CurrentLine { get; set; }
    public FakeDocumentLines Lines => new(this);
    public FakeHeaderUserFields UserFields => new(this);

    public bool GetByKey(int entry)
    {
        company.Events.Add($"get:{entry}");
        if (!company.Orders.TryGetValue(entry, out order)) return false;
        PendingOldCode = order.HeaderValue;
        PendingLines = order.Lines.Select(line => line.Copy()).ToList();
        CurrentLine = 0;
        return true;
    }

    public int Update()
    {
        company.Events.Add($"update:{order.DocEntry}");
        company.LastError = company.FailUpdateEntry == order.DocEntry ? -5002 : 0;
        if (company.LastError == 0)
        {
            order.HeaderValue = PendingOldCode;
            order.Lines = PendingLines.Select(line => line.Copy()).ToList();
        }
        return company.LastError;
    }

    public int Cancel()
    {
        company.Events.Add($"cancel:{order.DocEntry}");
        company.LastError = company.FailCancel ? -5002 : 0;
        if (company.LastError == 0) order.Cancelled = true;
        return company.LastError;
    }
}

public class FakeDocumentLines(FakeDocument document)
{
    public int Count => document.PendingLines.Count;
    public int LineNum => document.PendingLines[document.CurrentLine].LineNum;
    public FakeUserFields UserFields => new(document.PendingLines[document.CurrentLine]);

    public void SetCurrentLine(int index)
    {
        if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        document.CurrentLine = index;
    }
}

public class FakeHeaderUserFields(FakeDocument document)
{
    public FakeHeaderFields Fields => new(document);
}

public class FakeHeaderFields(FakeDocument document)
{
    public FakeHeaderField Item(string name)
    {
        if (name != "U_OLDCODE") throw new InvalidOperationException("Unknown header UDF.");
        return new FakeHeaderField(document);
    }
}

public class FakeHeaderField(FakeDocument document)
{
    public object Value
    {
        get => document.PendingOldCode;
        set => document.PendingOldCode = (string)value;
    }
}

public class FakeUserFields(FakeLine line)
{
    public FakeFields Fields => new(line);
}

public class FakeFields(FakeLine line)
{
    public FakeField Item(string name)
    {
        if (name != "U_SO_Ref" && name != "U_So_Line_Ref") throw new InvalidOperationException("Unknown UDF.");
        return new FakeField(line, name);
    }
}

public class FakeField(FakeLine line, string name)
{
    public object Value
    {
        get => name == "U_SO_Ref" ? line.U_SO_Ref : line.U_So_Line_Ref;
        set
        {
            if (name == "U_SO_Ref")
            {
                if (value is not string text || text.Length > 20)
                    throw new System.Runtime.InteropServices.COMException("Illegal value entered", -2055);
                line.U_SO_Ref = text;
            }
            else
            {
                if (value is not int number || number < short.MinValue || number > short.MaxValue)
                    throw new System.Runtime.InteropServices.COMException("Illegal value entered", -2055);
                line.U_So_Line_Ref = number;
            }
        }
    }
}

// The SDK interfaces are proxied in memory; no SAP COM server is instantiated.
public class SapMockProxy : DispatchProxy
{
    private object target;

    public static object Wrap(Type interfaceType, object instance)
    {
        object proxy = Create(interfaceType, typeof(SapMockProxy));
        ((SapMockProxy)proxy).target = instance;
        return proxy;
    }

    protected override object Invoke(MethodInfo method, object[] args)
    {
        args ??= [];
        object result;
        try
        {
            if (method.Name.StartsWith("get_", StringComparison.Ordinal))
                result = target.GetType().GetProperty(method.Name[4..]).GetValue(target);
            else if (method.Name.StartsWith("set_", StringComparison.Ordinal))
            {
                target.GetType().GetProperty(method.Name[4..]).SetValue(target, args[0]);
                return null;
            }
            else
            {
                MethodInfo targetMethod = target.GetType().GetMethods()
                    .Single(candidate => candidate.Name == method.Name && candidate.GetParameters().Length == args.Length);
                ParameterInfo[] parameters = targetMethod.GetParameters();
                for (int i = 0; i < args.Length; i++)
                {
                    if (!parameters[i].ParameterType.IsByRef && args[i] != null &&
                        !parameters[i].ParameterType.IsInstanceOfType(args[i]))
                        args[i] = Convert.ChangeType(args[i], parameters[i].ParameterType);
                }
                result = targetMethod.Invoke(target, args);
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        if (result is FakeDocument) return Wrap(typeof(SAPbobsCOM.Documents), result);
        if (result is FakeDocumentLines) return Wrap(typeof(SAPbobsCOM.Document_Lines), result);
        if (result is FakeUserFields || result is FakeHeaderUserFields) return Wrap(typeof(SAPbobsCOM.UserFields), result);
        if (result is FakeFields || result is FakeHeaderFields) return Wrap(typeof(SAPbobsCOM.Fields), result);
        if (result is FakeField || result is FakeHeaderField) return Wrap(typeof(SAPbobsCOM.Field), result);
        return result;
    }
}
