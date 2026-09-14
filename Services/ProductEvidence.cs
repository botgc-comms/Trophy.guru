namespace Trophy.Catalogue.Services;

// Public product examples only. The demo deliberately uses fictional records.
public static class ProductEvidence
{
    public static bool HasShowcase(string path) => path is "/demo" or "/electronic-honours-boards" or "/for-golf-clubs" or "/digitise-trophy-records" or "/how-it-works";

    public static string IntroActions(string path) => HasShowcase(path) ? """
        <div class="evidence-actions"><a class="evidence-button" href="/archive.html#signup">Try your first trophy free</a><a class="evidence-secondary" href="/demo#explore">Explore the demo</a></div>
        <p class="evidence-small">No payment card required. Your first trophy includes up to 12 saved photographs.</p>
        """ : "";

    public static string Showcase(string path) => !HasShowcase(path) ? "" : """
        <section class="product-evidence" id="explore" aria-labelledby="evidence-title">
          <div class="evidence-heading"><p class="eyebrow">See what members can explore</p><h2 id="evidence-title">A cabinet full of history. A board everyone can browse.</h2>
          <p>Open the example board and switch between years, trophies and people. It uses the same honours board interface as a published club archive. No account is needed to explore it.</p></div>
          <figure class="evidence-screen"><a href="/honours.html?demo=1" aria-label="Open the interactive example honours board"><img src="/marketing/honours-board-example.webp" width="1280" height="1040" alt="Trophy Guru example honours board with year, trophy and person views and a searchable list of winners" fetchpriority="high"></a>
          <figcaption>Product screenshot with fictional Northfield Golf Club records. Trophy pictures are illustrations. This is a demonstration, not a customer case study.</figcaption></figure>
          <div class="evidence-views">
            <a href="/honours.html?demo=1#year/1968"><strong>Browse a year</strong><span>See the competition winners from the same season.</span></a>
            <a href="/honours.html?demo=1#trophy"><strong>Open a trophy</strong><span>Follow the winners recorded for one cup or competition.</span></a>
            <a href="/honours.html?demo=1#person"><strong>Find a person</strong><span>Explore the honours associated with a winner in the archive.</span></a>
          </div>
          <p><a class="evidence-button" href="/honours.html?demo=1">Open the interactive example board <span aria-hidden="true">→</span></a></p>
        </section>
        """;

    public static string Pricing(string path) => !HasShowcase(path) ? "" : """
        <section class="evidence-offer" aria-labelledby="offer-title"><p class="eyebrow">Start with the silverware you already have</p><h2 id="offer-title">Try one trophy free. Build the archive at your club’s pace.</h2>
        <p>Your first trophy includes up to 12 saved photographs, AI-assisted readings, review and core archive features. Check the results on your own engravings before purchasing more trophy credits. No payment card is required for the first trophy.</p>
        <dl class="evidence-prices"><div><dt>First trophy</dt><dd>Free</dd></div><div><dt>1 additional trophy</dt><dd>£7.50</dd></div><div><dt>10 trophies</dt><dd>£60</dd></div><div><dt>50 trophies</dt><dd>£250</dd></div></dl>
        <p>Prices include VAT. Core archive credits are a one-time purchase, never expire and have no subscription or per-user fee. Collections of 150 or more trophies start at £525. Optional Intelligent Golf integration is a separate £299 per club per year; <a href="/integrations/intelligent-golf/">check its current availability and scope</a>.</p>
        <div class="evidence-actions"><a class="evidence-button" href="/archive.html#signup">Try your first trophy free</a><a href="/#pricing">Full pricing and what is included</a></div></section>
        """;

    public static string Extra(string path) => path switch
    {
        "/electronic-honours-boards" => """
        <section><h2>Digital honours board software: decide what your club needs</h2>
        <p>A display and an archive solve different parts of the problem. If your winners already sit in a reliable spreadsheet, your first priority may be presentation. If the history survives mainly on cups, shields and plates, the first job is recovering and checking those records. Trophy Guru joins that capture and review workflow to a shareable online honours board.</p>
        <div class="evidence-table-wrap"><table class="evidence-table"><caption>Questions to ask before choosing a system</caption><thead><tr><th scope="col">Your club’s requirement</th><th scope="col">What Trophy Guru provides</th></tr></thead><tbody>
        <tr><th scope="row">Recover old engraved winners</th><td>Photograph the inscriptions and review proposed names and years against the evidence.</td></tr>
        <tr><th scope="row">Search across a collection</th><td>Published honours can be explored by year, trophy and person.</td></tr>
        <tr><th scope="row">Keep control of the records</th><td>Edit the archive, confirm the history and export a CSV copy.</td></tr>
        <tr><th scope="row">Share the result online</th><td>A public honours board link, with the club controlling publication and withdrawal.</td></tr>
        <tr><th scope="row">Buy a clubhouse screen or installation</th><td>This offer is the archive and online board. Screen hardware and on-site installation are not included.</td></tr>
        </tbody></table></div><p>See the <a href="/demo">working digital honours board demonstration</a> or use the <a href="/trophy-archive-project-plan">club archive project plan</a> to agree the scope with your committee.</p></section>
        """,
        "/for-golf-clubs" => """
        <section><h2>Give your secretary, historian and members a shared starting point</h2>
        <p>A club secretary may have recent results, a past captain may recognise an initial, and a volunteer historian may know where an older honours book is kept. Bring those sources together around one trophy. Agree who photographs it, who checks the readings and who decides when it is ready to publish.</p>
        <p>Keep uncertainty visible while you investigate. Two engraved initials do not prove a member’s identity, and a missing year does not prove that a competition was cancelled. A checked archive is more useful to future committees than a complete-looking list built on guesses.</p>
        <p>Our <a href="/trophy-archive-project-plan">trophy archive project plan for a club committee</a> includes a pilot checklist, review responsibilities and a handover plan. The <a href="/uk/how-to-catalogue-trophy-winners/">cataloguing guide</a> covers the practical work at the cabinet.</p></section>
        """,
        "/digitise-trophy-records" => """
        <section><h2>Trophy engraving transcription needs more than ordinary OCR</h2>
        <p>OCR means optical character recognition: reading text from an image. A flat printed page usually gives it clear lines and even contrast. Trophy engraving can be shallow, reflective, worn or wrapped around a curved surface. That is why a photograph that looks attractive may still be a poor record of the lettering.</p>
        <p>Trophy Guru uses AI-assisted reading of your image set to propose the winner records. It is designed to help with trophy inscriptions and supporting club records; it is not a service for buying or engraving new trophies. Photograph each band with overlap, check unclear letters from another angle, and compare proposed names with a reliable club source before confirming them.</p>
        <p>For a practical photography and checking sequence, follow our <a href="/uk/how-to-catalogue-trophy-winners/">guide to cataloguing winners from trophies and honours boards</a>. Then <a href="/demo">see how reviewed records appear on the example board</a>.</p></section>
        """,
        "/demo" => """
        <section><h2>Three things to try in the demonstration</h2><ol class="evidence-task-list">
        <li><strong>Compare seasons.</strong> Open the year view and choose 1967, then 1968. The trophy cards show the winners recorded in each season.</li>
        <li><strong>Follow a competition.</strong> Switch to the trophy view and open the Captain’s Cup. Browse the seven years of example winners attached to that trophy.</li>
        <li><strong>Explore a winner’s history.</strong> Switch to the person view and select a name. The board brings that person’s recorded honours together across the example collection.</li>
        </ol><p>The example contains six trophies and 42 honours across 1962–1968. The names and results are invented to demonstrate navigation. They are not transcriptions of a real club’s records and do not demonstrate an accuracy rate.</p></section>
        """,
        "/trophy-archive-project-plan" => ProjectChecklist,
        _ => ""
    };

    private const string ProjectChecklist = """
        <section><h2>A committee checklist for the first trophy</h2><p>Use this at the cabinet or copy it into your project notes. Complete a small pilot before estimating the whole collection.</p>
        <ul class="evidence-task-list"><li>Record the trophy name, competition and where it is kept.</li><li>Note the earliest and latest visible years, including any gaps.</li><li>Photograph the complete trophy, then each engraved area with overlapping views.</li><li>Keep a list of supporting sources: honours books, minutes, result sheets or photographs.</li><li>Assign a reviewer who can compare each proposed name and year with its source.</li><li>Record unresolved initials or dates as questions, not confirmed facts.</li><li>Agree who can approve publication and who will maintain the archive next season.</li><li>Export a copy of the reviewed records and record where the club keeps it.</li></ul></section>
        <section><h2>Estimate the work from your own pilot</h2><p>Record the time spent gathering the trophy, photographing it, checking the reading and resolving exceptions. Count both straightforward trophies and difficult ones when planning the next batch. Do not multiply the speed of your easiest cup across a cabinet of worn engravings.</p>
        <div class="evidence-table-wrap"><table class="evidence-table"><caption>Simple project notes to complete during your pilot</caption><thead><tr><th scope="col">Record</th><th scope="col">What to write down</th></tr></thead><tbody><tr><th scope="row">Scope</th><td>Trophy, competition, visible date range and number of engraved areas.</td></tr><tr><th scope="row">Capture</th><td>Photographer, date, image count and time taken.</td></tr><tr><th scope="row">Review</th><td>Reviewer, time taken, corrections and unresolved entries.</td></tr><tr><th scope="row">Other evidence</th><td>Source title, date or page, and where the original can be found.</td></tr><tr><th scope="row">Approval</th><td>Who checked the final record and whether it is ready to share.</td></tr><tr><th scope="row">Handover</th><td>Archive owner, export location and next review date.</td></tr></tbody></table></div></section>
        <section><h2>Budget for a staged archive</h2><p>In Trophy Guru the first trophy is free, with up to 12 saved photographs and no payment card required. Additional credits cost £7.50 for one trophy, £60 for ten or £250 for fifty, including VAT. Credits do not expire. Allow separately for your volunteers’ time and any specialist handling or conservation advice your collection needs.</p><p><a href="/#pricing">Check the full current pricing</a>, <a href="/demo">show the committee the example honours board</a>, or <a href="/archive.html#signup">start the pilot with your first trophy free</a>.</p></section>
        """;
}
