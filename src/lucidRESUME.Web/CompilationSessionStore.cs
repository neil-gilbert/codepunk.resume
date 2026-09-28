using lucidRESUME.Compiler;
using Microsoft.Extensions.Caching.Memory;

namespace lucidRESUME.Web;

public sealed class CompilationSessionStore(IMemoryCache cache)
{
    public void Put(CompilationResult result, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(result);
        cache.Set(result.CompilationId, result, lifetime);
    }

    public bool TryGet(string id, out CompilationResult result) => cache.TryGetValue(id, out result!);
}
