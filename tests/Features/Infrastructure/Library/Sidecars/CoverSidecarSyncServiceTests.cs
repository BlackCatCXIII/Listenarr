using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Library.Sidecars
{
    [Trait("Name", "CoverSidecarSyncServiceTests")]
    [Trait("Category", "Library")]
    public class CoverSidecarSyncServiceTests : BaseTests
    {
        [Fact]
        public async Task SyncAsync_ReturnsDisabled_WhenFeatureIsOff()
        {
            var libraryRoot = FileService.GetTempPath();
            var bookPath = Path.Join(libraryRoot, "Author", "Book");
            Directory.CreateDirectory(bookPath);
            await SaveSettingsAsync(libraryRoot, enabled: false);
            await SeedRootFolderAsync(libraryRoot);
            SeedCachedLibraryImage("BOOK1", [1, 2, 3]);

            var service = _provider.GetRequiredService<ICoverSidecarSyncService>();
            var result = await service.SyncAsync(CreateAudiobook(bookPath, "/api/v1/images/BOOK1"));

            Assert.Equal(CoverSidecarSyncStatus.Disabled, result.Status);
            Assert.False(File.Exists(Path.Join(bookPath, "cover.jpg")));
        }

        [Fact]
        public async Task SyncAsync_WritesManagedCoverSidecar_WhenEnabled()
        {
            var libraryRoot = FileService.GetTempPath();
            var bookPath = Path.Join(libraryRoot, "Author", "Book");
            Directory.CreateDirectory(bookPath);
            await SaveSettingsAsync(libraryRoot);
            await SeedRootFolderAsync(libraryRoot);
            SeedCachedLibraryImage("BOOK1", [1, 2, 3, 4]);

            var service = _provider.GetRequiredService<ICoverSidecarSyncService>();
            var result = await service.SyncAsync(CreateAudiobook(bookPath, "/api/v1/images/BOOK1"));

            Assert.Equal(CoverSidecarSyncStatus.Written, result.Status);
            Assert.Equal([1, 2, 3, 4], await File.ReadAllBytesAsync(Path.Join(bookPath, "cover.jpg")));
            Assert.True(File.Exists(Path.Join(bookPath, ".listenarr-cover.json")));
        }

        [Fact]
        public async Task SyncAsync_UpdatesManagedCoverSidecar_WhenImageChanges()
        {
            var libraryRoot = FileService.GetTempPath();
            var bookPath = Path.Join(libraryRoot, "Author", "Book");
            Directory.CreateDirectory(bookPath);
            await SaveSettingsAsync(libraryRoot);
            await SeedRootFolderAsync(libraryRoot);
            SeedCachedLibraryImage("BOOK1", [1, 2, 3]);
            SeedCachedLibraryImage("BOOK2", [8, 9, 10]);

            var service = _provider.GetRequiredService<ICoverSidecarSyncService>();
            var audiobook = CreateAudiobook(bookPath, "/api/v1/images/BOOK1");

            await service.SyncAsync(audiobook);
            audiobook.ImageUrl = "/api/v1/images/BOOK2";
            var result = await service.SyncAsync(audiobook);

            Assert.Equal(CoverSidecarSyncStatus.Written, result.Status);
            Assert.Equal([8, 9, 10], await File.ReadAllBytesAsync(Path.Join(bookPath, "cover.jpg")));
        }

        [Fact]
        public async Task SyncAsync_PreservesExistingUnmanagedCoverSidecar()
        {
            var libraryRoot = FileService.GetTempPath();
            var bookPath = Path.Join(libraryRoot, "Author", "Book");
            Directory.CreateDirectory(bookPath);
            await SaveSettingsAsync(libraryRoot);
            await SeedRootFolderAsync(libraryRoot);
            SeedCachedLibraryImage("BOOK1", [1, 2, 3]);
            await File.WriteAllBytesAsync(Path.Join(bookPath, "cover.jpg"), [9, 9, 9]);

            var service = _provider.GetRequiredService<ICoverSidecarSyncService>();
            var result = await service.SyncAsync(CreateAudiobook(bookPath, "/api/v1/images/BOOK1"));

            Assert.Equal(CoverSidecarSyncStatus.Skipped, result.Status);
            Assert.Equal([9, 9, 9], await File.ReadAllBytesAsync(Path.Join(bookPath, "cover.jpg")));
            Assert.False(File.Exists(Path.Join(bookPath, ".listenarr-cover.json")));
        }

        private async Task SaveSettingsAsync(
            string libraryRoot,
            bool enabled = true,
            string fileName = "cover.jpg",
            bool overwriteManaged = true)
        {
            var builder = new ApplicationSettingsBuilder()
                .WithOutputPath(libraryRoot);
            if (enabled)
            {
                builder.WithCoverSidecars(fileName, overwriteManaged);
            }

            await _applicationSettingsRepository.SaveAsync(builder.Build());
        }

        private Task SeedRootFolderAsync(string libraryRoot)
            => _rootFolderRepository.AddAsync(new RootFolderBuilder()
                .WithName("Audiobooks")
                .WithPath(libraryRoot)
                .WithIsDefault()
                .Build());

        private string SeedCachedLibraryImage(string identifier, byte[] bytes)
        {
            var imagePath = Path.Join(
                _applicationPathService.ResolveFromConfig("cache", "images", "library"),
                $"{identifier}.jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
            File.WriteAllBytes(imagePath, bytes);
            return imagePath;
        }

        private static Audiobook CreateAudiobook(string basePath, string imageUrl)
            => new AudiobookBuilder()
                .WithId(123)
                .WithTitle("Test Book")
                .WithAuthor("Test Author")
                .WithBasePath(basePath)
                .WithImageUrl(imageUrl)
                .Build();
    }
}
