using nv_color_profiles.core.diagnostics;

namespace nv_color_profiles.core.tests.diagnostics;

public sealed class file_log_writer_tests : IDisposable
{
    private readonly string temp_dir;
    private readonly string log_file;

    public file_log_writer_tests()
    {
        temp_dir = Path.Combine(Path.GetTempPath(), "nvcp_test_" + Guid.NewGuid().ToString("N"));
        log_file = Path.Combine(temp_dir, "logs", "test.log");
    }

    [Fact]
    public void write_line_creates_file_and_appends()
    {
        var writer = new file_log_writer(log_file);

        writer.write_line("first");
        writer.write_line("second");

        var lines = File.ReadAllLines(log_file);
        Assert.Equal(new[] { "first", "second" }, lines);
    }

    [Fact]
    public void rotation_moves_full_log_to_backup()
    {
        // tiny threshold so a couple of lines trigger rotation
        var writer = new file_log_writer(log_file, max_bytes: 8);

        writer.write_line("0123456789"); // exceeds 8 bytes -> next write rotates
        writer.write_line("after-rotate");

        var rolled = log_file + ".1";
        Assert.True(File.Exists(rolled), "rolled-over backup should exist");
        Assert.Contains("0123456789", File.ReadAllText(rolled));
        Assert.Equal("after-rotate", File.ReadAllText(log_file).TrimEnd());
    }

    public void Dispose()
    {
        if (Directory.Exists(temp_dir))
        {
            Directory.Delete(temp_dir, recursive: true);
        }
    }
}
