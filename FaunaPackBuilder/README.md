# FaunaPack Builder 1.2.2

Builder Windows para criar `.faunapack` usando imagens e ocorrências do GBIF.

## Local da busca

O botão **📍 Local** abre uma janela secundária onde o usuário escolhe:

- **País (ISO-2)** — por padrão `BR`.
- **Estado/Província** — por padrão `Rio Grande do Sul`.

Se o estado/província ficar vazio, a busca usa somente o país.

Os filtros de fauna são mantidos automaticamente:

```text
country=<país>
stateProvince=<estado/província>   (quando informado)
kingdom=Animalia
media_type=StillImage
occurrenceStatus=present
taxonRank=SPECIES
```

A mesma localização é usada tanto para **descoberta das espécies** quanto para a **busca das imagens de referência**. Isso evita descobrir uma espécie em uma região e depois buscar as imagens fora dela.

## Outras correções incluídas

- descoberta por páginas de ocorrências, sem depender da faceta `speciesKey`;
- validação adicional de `Animalia` e `SPECIES` após consultar o taxon no GBIF;
- catálogo JSON salvo em UTF-8 sem BOM e com caracteres Unicode legíveis (por exemplo, `pavão` em vez de `pavão` escapado como `\\u00e3`);
- imagens mantêm a exigência configurada de 1 a 50 referências por espécie;
- referências licenciadas continuam sendo filtradas conforme as opções da interface.

### Observação sobre dados biológicos

O GBIF é usado aqui como fonte de ocorrência/taxonomia e mídia. Campos como altura, peso, dieta, habitat e curiosidades não são preenchidos com valores inventados: quando não houver uma fonte confiável integrada para um campo, ele permanece vazio. O enriquecimento desses campos deve usar fontes biológicas específicas e verificáveis.

## Build

```powershell
dotnet restore
dotnet build -c Release
```

Para publicar:

```powershell
.\build.ps1
```

A publicação usa `win-x64`, self-contained e mantém as DLLs nativas do ONNX Runtime junto do executável.
