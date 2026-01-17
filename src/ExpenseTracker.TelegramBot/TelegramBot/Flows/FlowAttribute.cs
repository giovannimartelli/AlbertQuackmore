namespace ExpenseTracker.TelegramBot.TelegramBot.Flows;

/// <summary>
/// Attribute to mark and name a flow handler.
/// The name is used in configuration to enable/disable the flow.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class FlowAttribute : Attribute
{
    public string Name { get; }

    public FlowAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Flow name cannot be null or empty", nameof(name));

        Name = name;
    }
}
