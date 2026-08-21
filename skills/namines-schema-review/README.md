# Kurulum — Namines Claude eklentisi

Skill tek başına yarım bir şey: "Destructive ise dur" diyor ama riski hesaplayacak
aracı içermiyor. Bu yüzden Skill ve MCP sunucusu **tek eklenti** olarak dağıtılıyor —
tek kurulumla ikisi birden gelir.

## 1. Eklenti olarak (önerilen)

Etkileşimli bir Claude Code oturumunda:

```
/plugin marketplace add enesimo16/Namines
```

```
/plugin install namines@namines
```

Gelenler:
- `namines-schema-review` skill'i (politika: hangi risk ne yapmayı zorunlu kılar)
- `namines` MCP sunucusu (`.mcp.json` üzerinden `npx -y @namines/mcp`)

`/plugin` etkileşimli bir panel açar; script'ten değil, terminaldeki `claude`
oturumundan çalıştırılır.

## 2. Sadece MCP sunucusu (skill olmadan)

MCP istemcisi yapılandırmasına:

```json
{
  "mcpServers": {
    "namines": {
      "command": "npx",
      "args": ["-y", "@namines/mcp"]
    }
  }
}
```

Araçlar çalışır ama politika katmanı olmaz — risk seviyelerinin ne yapmayı zorunlu
kıldığına dair kurallar yalnızca skill'de. Araç açıklamaları bunu taşıyamaz, çünkü
bir aracın açıklaması "ne yapabilirim"i anlatır, "ne zaman durmalıyım"ı değil.

## 3. Sadece skill (elle)

```bash
mkdir -p .claude/skills
cp -r skills/namines-schema-review .claude/skills/
```

Kullanıcı seviyesinde her projede geçerli olsun istenirse `.claude/skills` yerine
`~/.claude/skills` kullanılır.

Bu yolda MCP araçları **gelmez**; skill onlar olmadan yalnızca bir kontrol listesidir.

## Doğrulama

Kurduktan sonra, şema değişikliği isteyen bir istek skill'i devreye sokmalı ve
`namines_analyze_impact` çağrılmalı. Araçların göründüğünü doğrudan görmek için:

```bash
npx -y @namines/mcp
```

stdio bekler (JSON-RPC), yani boş görünmesi normaldir — hata vermeden açılıyorsa
binary indirilmiş ve çalışıyordur. İndirme ilerlemesi ve uyarılar stderr'e yazılır;
stdout protokol kanalıdır ve oraya hiçbir serbest metin yazılmaz.
