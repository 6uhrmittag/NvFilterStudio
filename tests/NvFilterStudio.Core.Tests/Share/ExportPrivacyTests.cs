using NvFilterStudio.Core.Model;
using NvFilterStudio.Core.Share;
using NvFilterStudio.Core.Tests.Store;

namespace NvFilterStudio.Core.Tests.Share;

/// <summary>
/// Guards the promise that an export is safe to hand to somebody else.
/// </summary>
/// <remarks>
/// SECURITY.md tells people exports carry no identifiers and says a test
/// asserts it. That test did not exist, and the claim was not even true: the
/// store lives under <c>%LOCALAPPDATA%</c>, and recording its literal path put
/// the sender's Windows account name into every file they shared.
/// <para>
/// Black-box on purpose. Testing the redaction helper directly would prove the
/// helper works while saying nothing about whether <see cref="ExportDocument"/>
/// actually calls it, and the guarantee people rely on is about the file.
/// </para>
/// </remarks>
public class ExportPrivacyTests
{
    private const string ExePath = @"C:\Games\Example\Example.exe";

    private static FilterPresetDocument Document() =>
        FilterPresetDocument.Parse(SyntheticStore.BuildPresetJson(ExePath, 10));

    [Fact]
    public void Export_DoesNotCarryTheUsersAccountName()
    {
        // The real store directory, which is the one the app records, and which
        // sits under the user's profile.
        string storePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NVIDIA Corporation",
            "NVIDIA Overlay");

        string export = ExportDocument.Create(
            Document(),
            null,
            new Dictionary<string, string> { ["storePath"] = storePath });

        string user = Environment.UserName;

        Assert.False(
            string.IsNullOrEmpty(user),
            "cannot prove anything without a user name to look for");

        Assert.DoesNotContain(user, export, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%LOCALAPPDATA%", export, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_KeepsTheStorePathUsefulAfterRedacting()
    {
        // Redacting must not reduce it to nothing: the path is worth having in a
        // bug report, it is only the account name that is not.
        string storePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NVIDIA Corporation",
            "NVIDIA Overlay");

        string export = ExportDocument.Create(
            Document(),
            null,
            new Dictionary<string, string> { ["storePath"] = storePath });

        Assert.Contains("NVIDIA Corporation", export, StringComparison.Ordinal);
        Assert.Contains("NVIDIA Overlay", export, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_LeavesUnrelatedSourceValuesAlone()
    {
        // Redaction is prefix-anchored, so anything that is not under the user's
        // profile has to survive untouched.
        string export = ExportDocument.Create(
            Document(),
            null,
            new Dictionary<string, string>
            {
                ["tool"] = "NvFilterStudio",
                ["recordSource"] = "000004.log",
            });

        Assert.Contains("NvFilterStudio", export, StringComparison.Ordinal);
        Assert.Contains("000004.log", export, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_CarriesNoAccountIdOrSessionGuid()
    {
        // The raw store holds a 43-character NVIDIA account id and session
        // GUIDs. They live outside the filterPresets document, so an export
        // cannot reach them - this pins that, since the export includes each
        // filter's verbatim `native` node and would carry anything hiding there.
        string export = ExportDocument.Create(Document(), null, null);

        Assert.DoesNotContain("userId", export, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionId", export, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            export);
    }
}
