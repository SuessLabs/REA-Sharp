using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Rea.Domain;
using Rea.Application;
using Rea.Domain.Managed;
using Rea.Managed;
using Rea.Validation;

namespace Rea.Tests;

internal static class UpstreamSyncTests
{
    public static IEnumerable<Case> Cases()
    {
        yield return Case.Sync("Upstream 10407fb6 rejects invalid UTF-8 metadata versions at their source offset", () =>
        {
            var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Rea.ManagedFixture.dll"));
            using var pe = new PEReader(new MemoryStream(bytes, false)); var offset = pe.PEHeaders.MetadataStartOffset;
            bytes[offset + 16] = 0xff;
            var result = Inspect(bytes);
            Check.True(result.Coverage.Issues.Any(issue => issue.Code == "invalid-metadata-root" && issue.Scope == "metadata.version" && issue.Offset == offset + 16));
            Check.True(result.Metadata.Version is null);
        });
        yield return Case.Sync("Upstream 10407fb6 preserves valid multibyte metadata versions without executing targets", () =>
        {
            var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Rea.ManagedFixture.dll"));
            using var pe = new PEReader(new MemoryStream(bytes, false)); var offset = pe.PEHeaders.MetadataStartOffset;
            var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 12));
            bytes.AsSpan(offset + 16, length).Clear(); Encoding.UTF8.GetBytes("v😀é").CopyTo(bytes, offset + 16);
            var result = Inspect(bytes); Check.Equal("v😀é", result.Metadata.Version); Check.True(result.Assembly is not null);
        });
        yield return Case.Sync("Upstream Hexagon ELF admission preserves byte order and ELF class", () =>
        {
            foreach (var wide in new[] { false, true }) foreach (var little in new[] { false, true })
                Check.Equal("hexagon", Check.Success(ExecutableHeader.Parse(Fixtures.Elf(wide, little, 164), "x64")).Architecture);
        });
        yield return Case.Sync("Upstream thin arm64_32 Mach-O identification remains format specific", () =>
        {
            foreach (var little in new[] { false, true })
            {
                var parsed = Check.Success(ExecutableHeader.Parse(Fixtures.ThinMach(little: little, cpu: 0x0200000c), "x64"));
                Check.Equal("mach-o", parsed.Format); Check.Equal("arm64_32", parsed.Architecture);
            }
        });
        yield return Case.Sync("Upstream Mach-O command framing checks counts alignment and retained extent", () =>
        {
            foreach (var wide in new[] { false, true }) foreach (var little in new[] { false, true })
            {
                var header = wide ? 32 : 28; var bytes = new byte[header + 16];
                Fixtures.ThinMach(wide, little).CopyTo(bytes, 0); Fixtures.U32(bytes, 16, 1, little); Fixtures.U32(bytes, 20, 16, little); Fixtures.U32(bytes, header + 4, 16, little);
                Check.Success(ExecutableHeader.Parse(bytes, "x64"));
                foreach (var size in new uint[] { 0, 4, 9, 24 })
                { var invalid = (byte[])bytes.Clone(); Fixtures.U32(invalid, header + 4, size, little); Check.Failure(ExecutableHeader.Parse(invalid, "x64")); }
                Fixtures.U32(bytes, 16, 2, little); Check.Failure(ExecutableHeader.Parse(bytes, "x64"));
                Fixtures.U32(bytes, 16, 1, little);
                Check.Success(ExecutableHeader.Parse(bytes.AsSpan(0, header), "x64", bytes.Length));
            }
        });
        yield return Case.Sync("Upstream JVM interface identity rejects UTF8 constants as class indexes", () =>
        {
            var original = Fixtures.JvmClass(); var insertion = original.Length - 6;
            var bytes = new byte[original.Length + 2]; original.AsSpan(0, insertion).CopyTo(bytes); original.AsSpan(insertion).CopyTo(bytes.AsSpan(insertion + 2));
            Fixtures.U16(bytes, insertion - 2, 1, false); Fixtures.U16(bytes, insertion, 2, false);
            Check.Equal("unsupported binary format", Check.Failure(ExecutableHeader.Parse(bytes, "x64")));
            Fixtures.U16(bytes, insertion, 1, false);
            Check.True(Check.Failure(ExecutableHeader.Parse(bytes, "x64")) != "unsupported binary format");
        });
        yield return Case.Sync("Upstream DOS relocation padding does not invent Windows header declarations", () =>
        {
            var bytes = Fixtures.DosMz(); Fixtures.U16(bytes, 8, 4); Fixtures.U16(bytes, 6, 1);
            Check.True(DosMz.ObserveWindowsHeader(bytes, bytes.Length) is MzWindowsHeaderObservation.Absent);
            Check.Equal("dos-mz", Check.Success(ExecutableHeader.Parse(bytes, "x64")).Format);
            Fixtures.U32(bytes, 60, 100);
            Check.True(DosMz.ObserveWindowsHeader(bytes, bytes.Length, "NE"u8) is MzWindowsHeaderObservation.Declared);
            Check.True(DosMz.ObserveWindowsHeader(bytes.AsSpan(0, 64), bytes.Length) is MzWindowsHeaderObservation.SignatureNeeded);
            Fixtures.U16(bytes, 6, 0);
            Check.True(DosMz.ObserveWindowsHeader(bytes, bytes.Length) is MzWindowsHeaderObservation.Candidate);
        });
        yield return Case.Sync("Upstream inventory requires valid pinned identities and preserves changed-path status", () =>
        {
            var sha = new string('a', 40);
            var commits = UpstreamSync.ParseCommits(sha + "\0" + "2026-11-06T00:00:00Z\0subject\0\n");
            Check.Equal(1, commits.Length); Check.Equal("pending-semantic-review", commits[0].Status);
            var paths = UpstreamSync.ParseChanges("M\0src/domain/elfFixedHeader.ts\0A\0src/new-provider.ts\0");
            Check.Equal("dotnet/src/Rea.Domain/ElfHeader.cs", paths[0].CSharpOwner); Check.True(paths[1].CSharpOwner is null);
            Check.True(!UpstreamSync.IsRevision("--all"));
            var rejected = false; try { UpstreamSync.ParseChanges("M\0../escape\0"); } catch (InvalidDataException) { rejected = true; }
            Check.True(rejected);
        });
    }
    private static ManagedArtifactInspection Inspect(byte[] bytes) => Check.Success(new ManagedArtifactInspector().Inspect(ManagedArtifactSnapshot.Create("upstream-fixture.dll", bytes), default));
}

