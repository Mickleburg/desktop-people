using DesktopPeople.Tests;

TestCase[] tests =
[
    .. LegacyTests.All,
    .. PlatformCoreTests.All,
    .. WindowAdapterTests.All,
    .. CharacterSimulationTests.All,
];

int failures = 0;
foreach (TestCase test in tests)
{
    try
    {
        test.Execute();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL  {test.Name}");
        Console.Error.WriteLine(exception);
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;
