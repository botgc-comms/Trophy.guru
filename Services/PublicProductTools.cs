using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Trophy.Catalogue.Services;

[McpServerToolType]
public sealed class PublicProductTools
{
    // Fixed content and URLs. No input, account context, database or network calls.
    public static object Knowledge() => new {
        overview = ProductPages.Overview,
        company = "Marabou Stork Limited",
        workflow = ProductPages.All.Single(p => p.Path == "/how-it-works").Sections,
        faqs = ProductPages.Faqs,
        pages = ProductPages.All.Select(p => new { title = p.Heading, url = "https://trophy.guru" + p.Path }),
        demoUrl = "https://trophy.guru/#member-experience",
        signupUrl = "https://trophy.guru/archive.html#signup"
    };

    [McpServerTool(Name = "get_trophy_guru_overview", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Explain software for digitising engraved trophy records and historical competition winners into electronic honours boards for golf clubs and sporting organisations. Returns public product facts and source links; no customer records.")]
    public static object Overview() => new { description = ProductPages.Overview, producer = "Marabou Stork Limited", url = "https://trophy.guru/about" };

    [McpServerTool(Name = "get_how_it_works", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Explain how a club can turn photographs of engraved trophies and trophy plates into reviewed historical winner records and an online honours board. Returns the public workflow; does not upload, transcribe or publish anything.")]
    public static object Workflow() => new { steps = ProductPages.All.Single(p => p.Path == "/how-it-works").Sections, url = "https://trophy.guru/how-it-works" };

    [McpServerTool(Name = "get_frequently_asked_questions", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Read factual answers about preserving historic trophy winners, using engraved plates, reviewing uncertain readings, exporting records and electronic honours boards. Returns public FAQs only.")]
    public static object Faq() => new { questions = ProductPages.Faqs, url = "https://trophy.guru/faq" };

    [McpServerTool(Name = "get_demo_links", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Find the existing illustrative electronic honours board and account signup page to explore trophy digitisation. Returns links only; does not request a demo, contact anyone or create an account.")]
    public static object Demo() => new { demoUrl = "https://trophy.guru/#member-experience", signupUrl = "https://trophy.guru/archive.html#signup", example = "Illustrative honours board, not a customer endorsement" };
}
