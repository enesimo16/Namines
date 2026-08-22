using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Namines.Tests.Security;

/// <summary>
/// Controller sözleşmesi.
///
/// <b>Bu testler ÜÇÜNCÜ kez tekrarlanan bir hata yüzünden var.</b> Bir
/// <c>[ApiController]</c> özniteliği yanlışlıkla bir <c>record</c>'un üstüne
/// düştüğünde ASP.NET o record'u controller sanıyor, <c>Deconstruct</c> metodunu
/// bir action olarak okuyor ve <b>uygulama hiç BAŞLAMIYOR</b> — 857 test yeşilken.
/// Birim testleri bunu göremez, çünkü hata tip yükleme sırasında değil, MVC'nin
/// keşif aşamasında çıkıyor.
///
/// Aynı şekilde <c>[ApiController]</c>/<c>[Route]</c>'u UNUTMAK da sessiz: uygulama
/// başlar, ama o controller'ın uçları hiçbir adrese bağlanmaz ve her çağrı 404
/// döner. İkisi de "derlenir, testler geçer, üretimde çalışmaz" sınıfında.
/// </summary>
public class ApiControllerConventionTests
{
    private static readonly Assembly ApiAssembly = typeof(Namines.API.Controllers.CompileController).Assembly;

    private static bool IsController(Type t) =>
        typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract;

    [Fact]
    public void The_api_controller_attribute_is_only_on_actual_controllers()
    {
        var strays = ApiAssembly.GetTypes()
            .Where(t => t.GetCustomAttribute<ApiControllerAttribute>(inherit: false) is not null)
            .Where(t => !IsController(t))
            .Select(t => t.FullName!)
            .ToList();

        Assert.True(strays.Count == 0,
            "These types carry [ApiController] but are not controllers, which stops the app from starting: " +
            string.Join(", ", strays));
    }

    [Fact]
    public void Every_controller_is_routable()
    {
        var unroutable = ApiAssembly.GetTypes()
            .Where(IsController)
            .Where(t => t.GetCustomAttribute<RouteAttribute>(inherit: true) is null &&
                        t.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any() == false)
            .Select(t => t.FullName!)
            .ToList();

        // Rota olmadan controller derlenir ve uygulama başlar; yalnızca her çağrı
        // 404 döner — hatanın en pahalı biçimi.
        Assert.True(unroutable.Count == 0,
            "These controllers have no [Route], so every call to them returns 404: " +
            string.Join(", ", unroutable));
    }

    [Fact]
    public void Every_controller_declares_the_api_controller_attribute()
    {
        var missing = ApiAssembly.GetTypes()
            .Where(IsController)
            .Where(t => t.GetCustomAttribute<ApiControllerAttribute>(inherit: true) is null)
            .Select(t => t.FullName!)
            .ToList();

        // [ApiController] olmadan model doğrulama otomatik 400 üretmez; geçersiz
        // gövde sessizce varsayılan değerlerle işlenir.
        Assert.True(missing.Count == 0,
            "These controllers are missing [ApiController]: " + string.Join(", ", missing));
    }
}
