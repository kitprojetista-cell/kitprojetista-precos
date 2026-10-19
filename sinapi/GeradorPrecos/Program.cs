using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

// Uso: GeradorPrecos <SINAPI-AAAA-MM-formato-xlsx.zip | SINAPI_Referência_AAAA_MM.xlsx> [saida\precos.json]
//
// Lê a aba ISD (insumos, sem desoneração; para materiais o preço é o mesmo nas outras abas) e escreve o
// preço de cada material do app por estado, já na unidade que o app usa (saco, milheiro, lata...).

if (args.Length < 1)
{
    Console.WriteLine("Uso: GeradorPrecos <arquivo .zip ou .xlsx do SINAPI> [precos.json]");
    return 1;
}
string entrada = args[0];
string saida = args.Length > 1 ? args[1] : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(entrada)) ?? ".", "precos.json");

// chave do app, código SINAPI, fator (unidade SINAPI → unidade do app), nome
var itens = new (string Chave, int Codigo, double Fator, string Nome)[]
{
    ("cimento50", 1379, 50, "Cimento Portland CP II-32, saco de 50 kg"),
    ("areia", 370, 1, "Areia média, m³ (retirada na jazida, sem frete)"),
    ("brita", 4721, 1, "Pedra britada nº 1, m³ (na pedreira, sem frete)"),
    ("tijolo-baiano", 7271, 1000, "Tijolo cerâmico 8 furos 9×19×19 cm, milheiro"),
    ("tijolo-macico", 7258, 1000, "Tijolo maciço comum 5×10×20 cm, milheiro"),
    ("bloco14", 651, 1, "Bloco de concreto de vedação 14×19×39 cm, unidade"),
    ("rejunte", 34357, 1, "Rejunte cimentício, kg"),
    ("argamassa20", 1381, 20, "Argamassa colante AC I, saco de 20 kg"),
    ("tinta18", 35692, 18, "Tinta látex acrílica standard, lata de 18 L"),
    ("tinta36", 35692, 3.6, "Tinta látex acrílica standard, galão de 3,6 L"),
    ("telha", 7175, 1000, "Telha cerâmica romana/francesa, milheiro"),
    // Material elétrico (Elétrica NBR 5410 › Material elétrico)
    ("cabo1.5", 1013, 1, "Cabo de cobre flexível 1,5 mm² 450/750 V, m"),
    ("cabo2.5", 1014, 1, "Cabo de cobre flexível 2,5 mm² 450/750 V, m"),
    ("cabo4", 981, 1, "Cabo de cobre flexível 4 mm² 450/750 V, m"),
    ("cabo6", 982, 1, "Cabo de cobre flexível 6 mm² 450/750 V, m"),
    ("cabo10", 980, 1, "Cabo de cobre flexível 10 mm² 450/750 V, m"),
    ("cabo16", 979, 1, "Cabo de cobre flexível 16 mm² 450/750 V, m"),
    ("eletroduto25", 2688, 1, "Eletroduto PVC flexível corrugado 25 mm, m"),
    ("eletroduto32", 2690, 1, "Eletroduto PVC flexível corrugado 32 mm, m"),
    ("caixa4x2", 1872, 1, "Caixa de passagem PVC 4\"×2\" para corrugado, unidade"),
    ("caixaoct", 1871, 1, "Caixa octogonal PVC 3\"×3\" fundo móvel, unidade"),
    ("tomada10", 7528, 1, "Tomada 2P+T 10 A, conjunto 4\"×2\" com placa, unidade"),
    ("tomada20", 38075, 1, "Tomada 2P+T 20 A, conjunto 4\"×2\" com placa, unidade"),
    ("interruptor", 38062, 1, "Interruptor simples 10 A, conjunto 4\"×2\" com placa, unidade"),
    ("disj-mono32", 34653, 1, "Disjuntor DIN monopolar 6 a 32 A, unidade"),
    ("disj-mono50", 34686, 1, "Disjuntor DIN monopolar 40 a 50 A, unidade"),
    ("disj-mono63", 34688, 1, "Disjuntor DIN monopolar 63 A, unidade"),
    ("disj-bi32", 34616, 1, "Disjuntor DIN bipolar 6 a 32 A, unidade"),
    ("disj-bi50", 34623, 1, "Disjuntor DIN bipolar 40 a 50 A, unidade"),
    ("disj-bi63", 34628, 1, "Disjuntor DIN bipolar 63 A, unidade"),
    ("dr25", 39445, 1, "DR bipolar 25 A 30 mA, unidade"),
    ("dr40", 39446, 1, "DR bipolar 40 A 30 mA, unidade"),
    ("dr63", 39447, 1, "DR bipolar 63 A 30 mA, unidade"),
    ("dr80", 39448, 1, "DR bipolar 80 A 30 mA, unidade"),
    ("dr100", 39459, 1, "DR bipolar 100 A 30 mA, unidade"),
    ("quadro8", 39804, 1, "Quadro de distribuição PVC de embutir, 8 disjuntores DIN, unidade"),
    ("quadro16", 39805, 1, "Quadro de distribuição PVC de embutir, 16 disjuntores DIN, unidade"),
    ("quadro24", 39806, 1, "Quadro de distribuição PVC de embutir, 24 disjuntores DIN, unidade"),
    ("quadro36", 39807, 1, "Quadro de distribuição PVC de embutir, 36 disjuntores DIN, unidade"),
    ("quadro48", 43100, 1, "Quadro de distribuição PVC de embutir, 48 disjuntores DIN, unidade"),
};

using var xlsx = AbrirPlanilha(entrada);
var tabela = LerAba(xlsx, "ISD");

// Mês de referência ("Mês de Referência:" | "09/2026") e a linha de cabeçalho ("Classificação" | "Código do Insumo" ...).
string mes = "";
int linhaCab = -1;
for (int i = 0; i < Math.Min(tabela.Count, 30); i++)
{
    var l = tabela[i];
    if (Celula(l, 0).StartsWith("Mês de Refer", StringComparison.OrdinalIgnoreCase))
    {
        var p = Celula(l, 1).Split('/');
        if (p.Length == 2) mes = p[1] + "-" + p[0].PadLeft(2, '0');
    }
    if (Celula(l, 0).StartsWith("Classifica", StringComparison.OrdinalIgnoreCase) && Celula(l, 1).StartsWith("C")) { linhaCab = i; break; }
}
if (linhaCab < 0 || mes.Length == 0) { Console.WriteLine("Não reconheci a planilha (aba ISD sem cabeçalho ou mês)."); return 2; }

var cab = tabela[linhaCab];
var ufs = new Dictionary<string, int>();
for (int c = 0; c < cab.Length; c++)
{
    string v = Celula(cab, c).Trim();
    if (v.Length == 2 && v.All(char.IsUpper)) ufs[v] = c;
}

var porCodigo = new Dictionary<int, string[]>();
for (int i = linhaCab + 1; i < tabela.Count; i++)
    if (int.TryParse(Celula(tabela[i], 1), out int cod)) porCodigo[cod] = tabela[i];

var precos = new SortedDictionary<string, SortedDictionary<string, double>>();
var faltando = new List<string>();
foreach (var it in itens)
{
    if (!porCodigo.TryGetValue(it.Codigo, out var linha)) { faltando.Add(it.Chave + " (código " + it.Codigo + " não está na planilha)"); continue; }
    int com = 0;
    foreach (var (uf, col) in ufs)
    {
        if (!double.TryParse(Celula(linha, col), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v <= 0) continue;
        if (!precos.TryGetValue(uf, out var d)) precos[uf] = d = new SortedDictionary<string, double>();
        d[it.Chave] = Math.Round(v * it.Fator, 2);
        com++;
    }
    Console.WriteLine($"{it.Chave,-14} {it.Codigo,6}  {com,2} estados  {Celula(linha, 2)}");
}

var json = new
{
    versao = 1,
    mes,
    fonte = "SINAPI (Caixa), preços medianos de insumos",
    itens = itens.ToDictionary(i => i.Chave, i => i.Nome),
    precos,
};
File.WriteAllText(saida, JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), new UTF8Encoding(false));
Console.WriteLine();
Console.WriteLine("Mês " + mes + ": " + precos.Count + " estados. Arquivo: " + Path.GetFullPath(saida));
foreach (var f in faltando) Console.WriteLine("ATENÇÃO: " + f);
return faltando.Count == 0 ? 0 : 3;

// ───────── leitura de .xlsx sem bibliotecas ─────────

static ZipArchive AbrirPlanilha(string caminho)
{
    if (caminho.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) return ZipFile.OpenRead(caminho);
    // .zip do SINAPI: a planilha de referência está dentro.
    var pacote = ZipFile.OpenRead(caminho);
    var e = pacote.Entries.FirstOrDefault(x => x.Name.StartsWith("SINAPI_Refer", StringComparison.OrdinalIgnoreCase) && x.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException("O .zip não tem a planilha SINAPI_Referência_*.xlsx");
    var mem = new MemoryStream();
    using (var s = e.Open()) s.CopyTo(mem);
    mem.Position = 0;
    return new ZipArchive(mem, ZipArchiveMode.Read);
}

static List<string[]> LerAba(ZipArchive x, string nome)
{
    XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    XNamespace pr = "http://schemas.openxmlformats.org/package/2006/relationships";
    XDocument Doc(string p) { using var s = x.GetEntry(p)!.Open(); return XDocument.Load(s); }

    var wb = Doc("xl/workbook.xml");
    var aba = wb.Descendants(ns + "sheet").FirstOrDefault(s => (string?)s.Attribute("name") == nome) ?? throw new InvalidDataException("Aba " + nome + " não encontrada");
    string id = (string)aba.Attribute(r + "id")!;
    var rel = Doc("xl/_rels/workbook.xml.rels").Descendants(pr + "Relationship").First(e => (string?)e.Attribute("Id") == id);
    string alvo = ((string)rel.Attribute("Target")!).TrimStart('/');
    if (!alvo.StartsWith("xl/")) alvo = "xl/" + alvo;

    var compartilhadas = new List<string>();
    if (x.GetEntry("xl/sharedStrings.xml") != null)
        foreach (var si in Doc("xl/sharedStrings.xml").Root!.Elements(ns + "si"))
            compartilhadas.Add(string.Concat(si.Descendants(ns + "t").Select(t => t.Value)));

    var linhas = new List<string[]>();
    foreach (var row in Doc(alvo).Descendants(ns + "row"))
    {
        var celulas = new SortedDictionary<int, string>();
        foreach (var c in row.Elements(ns + "c"))
        {
            string refc = (string?)c.Attribute("r") ?? "";
            int col = 0;
            foreach (char ch in refc) { if (!char.IsLetter(ch)) break; col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1); }
            string tipo = (string?)c.Attribute("t") ?? "";
            string v = tipo switch
            {
                "s" => int.TryParse(c.Element(ns + "v")?.Value, out int k) && k < compartilhadas.Count ? compartilhadas[k] : "",
                "inlineStr" => string.Concat(c.Descendants(ns + "t").Select(t => t.Value)),
                _ => c.Element(ns + "v")?.Value ?? "",
            };
            celulas[col - 1] = v;
        }
        var arr = new string[celulas.Count == 0 ? 0 : celulas.Keys.Max() + 1];
        for (int i = 0; i < arr.Length; i++) arr[i] = celulas.TryGetValue(i, out var s) ? s : "";
        linhas.Add(arr);
    }
    return linhas;
}

static string Celula(string[] l, int i) => i < l.Length ? l[i] : "";
