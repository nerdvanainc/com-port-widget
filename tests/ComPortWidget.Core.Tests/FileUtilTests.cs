using ComPortWidget.Core.Storage;

namespace ComPortWidget.Core.Tests;

public class FileUtilTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cpw-test-" + Guid.NewGuid().ToString("N"));

    public FileUtilTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void First_save_moves_the_temp_file_when_no_target_exists()
    {
        var temp = Path.Combine(_dir, "a.tmp");
        var dest = Path.Combine(_dir, "a.json");
        File.WriteAllText(temp, "first");
        FileUtil.ReplaceAtomic(temp, dest);
        Assert.False(File.Exists(temp));
        Assert.Equal("first", File.ReadAllText(dest));
    }

    [Fact]
    public void Existing_target_is_replaced()
    {
        var temp = Path.Combine(_dir, "b.tmp");
        var dest = Path.Combine(_dir, "b.json");
        File.WriteAllText(dest, "old");
        File.WriteAllText(temp, "new");
        FileUtil.ReplaceAtomic(temp, dest);
        Assert.Equal("new", File.ReadAllText(dest));
        Assert.Equal(new[] { dest }, Directory.GetFiles(_dir));
    }

    [Fact]
    public void Missing_temp_file_throws_an_io_error()
    {
        Assert.ThrowsAny<IOException>(() => FileUtil.ReplaceAtomic(Path.Combine(_dir, "none.tmp"), Path.Combine(_dir, "c.json")));
    }

    [Fact]
    public void Target_held_without_delete_sharing_fails_and_keeps_the_old_content()
    {
        var temp = Path.Combine(_dir, "d.tmp");
        var dest = Path.Combine(_dir, "d.json");
        File.WriteAllText(dest, "old");
        File.WriteAllText(temp, "new");
        using (new FileStream(dest, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsAny<Exception>(() => FileUtil.ReplaceAtomic(temp, dest));
        }
        Assert.Equal("old", File.ReadAllText(dest));
    }

    [Fact]
    public void Target_is_never_missing_while_it_is_replaced_repeatedly()
    {
        var dest = Path.Combine(_dir, "e.json");
        File.WriteAllText(dest, "0");
        var stop = false;
        var missing = 0;
        var reads = 0;
        var reader = new Thread(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                if (!File.Exists(dest)) Interlocked.Increment(ref missing);
                Interlocked.Increment(ref reads);
            }
        });
        reader.Start();
        try
        {
            for (var i = 1; i <= 500; i++)
            {
                var temp = Path.Combine(_dir, "e." + i + ".tmp");
                File.WriteAllText(temp, i.ToString());
                // 읽는 쪽이 같은 순간에 파일을 조회하면 바꾸기가 잠깐 거부될 수 있다. 그때는 다시 시도한다(대상이 사라지지는 않는다)
                for (var attempt = 0; ; attempt++)
                {
                    try { FileUtil.ReplaceAtomic(temp, dest); break; }
                    catch (Exception e) when ((e is UnauthorizedAccessException || e is IOException) && attempt < 100) { Thread.Sleep(1); }
                }
            }
        }
        finally
        {
            Volatile.Write(ref stop, true);
            reader.Join();
        }
        Assert.True(reads > 1000, "reader did not run enough: " + reads);
        Assert.Equal(0, missing);
        Assert.Equal("500", File.ReadAllText(dest));
    }
}
