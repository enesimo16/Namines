using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Namines.API.Controllers;
using Namines.Core.Models;

namespace Namines.Tests.Controllers;

/// <summary>
/// github/F4 — durumsuz merge önizlemesi.
///
/// <b>Neden durumsuz bir uç var:</b> canvas'taki branch'ler tarayıcıda yaşıyor
/// ve sunucuda bir kimlikleri yok (bkz. <c>ProjectSnapshot.branches</c>). Üç
/// şemayı gövdede alan bu uç, iki branch sistemini birleştirmeden canvas'ın
/// birleştirmesini üç yollu hâle getiriyor.
/// </summary>
public class MergeControllerTests
{
    private static SchemaColumn Column(string uuid, string name, string type = "int") =>
        new() { Id = uuid, StableUuid = uuid, Name = name, Type = type };

    private static DatabaseSchema Schema(params SchemaColumn[] columns) => new()
    {
        Tables = new List<SchemaTable>
        {
            new() { Id = "t1", StableUuid = "t1", Name = "users", Columns = columns.ToList() },
        },
    };

    [Fact]
    public void A_change_only_one_side_made_needs_no_decision()
    {
        var request = new MergePreviewRequest(
            Schema(Column("c1", "email", "varchar")),
            Schema(Column("c1", "email", "varchar")),
            Schema(Column("c1", "email", "text")));

        var result = new MergeController().Preview(request) as OkObjectResult;

        Assert.NotNull(result);
        var json = JsonSerializer.Serialize(result!.Value);
        Assert.Contains("\"conflicts\":[]", json);
        Assert.Contains("\"blocked\":false", json);
    }

    [Fact]
    public void The_conflict_kind_crosses_the_boundary_as_a_string()
    {
        // İstemcide birebir kopyalanmış bir birlik tipiyle eşlemek bu projede
        // bir kez kırıldı: sunucuya yeni bir değer eklenince kopya sessizce
        // yanlış değeri gösterdi.
        var request = new MergePreviewRequest(
            Schema(),
            Schema(Column("c-a", "status", "varchar")),
            Schema(Column("c-b", "status", "int")));

        var result = new MergeController().Preview(request) as OkObjectResult;

        Assert.Contains("\"kind\":\"NameCollision\"", JsonSerializer.Serialize(result!.Value));
    }

    [Fact]
    public void A_request_without_all_three_schemas_is_refused()
    {
        // Ortak ata olmadan "üç yollu birleştirdik" demek, iki yollu bir diff'i
        // yeni bir adla satmak olurdu.
        var result = new MergeController().Preview(
            new MergePreviewRequest(null!, Schema(), Schema()));

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
