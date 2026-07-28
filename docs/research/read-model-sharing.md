# Read-model sharing: one projection type per query, or one per aggregate?

Research note (2026-07-28). Primary sources only — author-owned blogs, their own books/papers, and their own source repos. No tutorials, Medium posts, or StackOverflow paraphrases were used; where a claim could not be reached in a primary source, that is stated rather than substituted.

Every quotation below was verified against raw page text or `pdftotext` extraction of the original PDF, not against a summarizer's output. Two summarizer passes produced a fabricated Greg Young quote and a plausible-but-absent Dahan attribution; both were discarded (see *Negative findings*).

## The question

- Each aggregate has an **internal EF Core projection type** — `internal sealed record InvoiceRow(...)` with `public static Expression<Func<Invoice, InvoiceRow>> Projection`, consumed as `.Select(InvoiceRow.Projection)`.
- It is **never persisted, never serialized, never on the wire**. Its only job is to make EF emit a narrow `SELECT` instead of materializing the aggregate graph.
- In this repo one such type is shared by several queries over one aggregate:

  | Row type | Query handlers sharing it |
  |---|---|
  | `InvoiceRow` | `GetInvoiceById`, `GetInvoiceByOrderId`, `GetInvoicesByBuyer` |
  | `PaymentTransactionRow` | `GetPaymentById`, `GetPaymentsByOrder` |
  | `ProductDetailRow` | `GetProductById`, `GetProductsByIds` |
  | `ProductSearchResultRow` | `GetProductsByCategory`, `SearchProducts` |

- **Share one type across those queries, or duplicate per vertical slice?**

Repo jurisdiction is already settled and is *not* the subject here: [ADR-0037](../adr/0037-endpoint-owned-response-contracts.md) governs the published wire contract and explicitly scopes these `*Row` types **out**; [ADR-0021 § Risks](../adr/0021-read-side-no-specifications.md) currently **permits** the sharing, mitigated by a handler comment and integration tests.

## 1. Jimmy Bogard — Vertical Slice Architecture

The only authority of the three who addresses **per-request type ownership directly**.

- **The coupling rule.** "Minimize coupling between slices, and maximize coupling in a slice." — Jimmy Bogard, [Vertical Slice Architecture](https://www.jimmybogard.com/vertical-slice-architecture/). Stated in the context of coupling *along the axis of change* rather than across a layer.
- **On shared types specifically — he does address it.** Under "Modeling": "AVOID sharing DTOs across multiple maps" — Jimmy Bogard, [AutoMapper Usage Guidelines](https://www.jimmybogard.com/automapper-usage-guidelines/). His stated rationale: changing a shared DTO can accidentally affect another request; modelling per request confines a change to that request. A neighbouring guideline prescribes *inner* types for non-flattenable members expressly so DTOs are not accidentally shared with other requests.
- **What he permits sharing.** The VSA post argues that layer-shaped abstractions (repositories, services) largely melt away, and that cross-slice *logic* sharing is minimized. It never enumerates a permitted set of shared **types** — that list comes from his code, not his prose.
- **What the code does — the strongest evidence.** In [ContosoUniversityDotNetCore-Pages](https://github.com/jbogard/ContosoUniversityDotNetCore-Pages) (branch `main`), every slice declares its own nested `Query`/`Command`/`Result`/`Model` **and** its own nested `MappingProfile`. No projection destination type is shared between two pages — *including when they are field-for-field identical*:
  - [`Pages/Instructors/Details.cshtml.cs`](https://raw.githubusercontent.com/jbogard/ContosoUniversityDotNetCore-Pages/main/ContosoUniversity/Pages/Instructors/Details.cshtml.cs) declares `Details.Model`; [`Pages/Instructors/Delete.cshtml.cs`](https://raw.githubusercontent.com/jbogard/ContosoUniversityDotNetCore-Pages/main/ContosoUniversity/Pages/Instructors/Delete.cshtml.cs) declares `Delete.Command`. Both carry `Id?`, `LastName`, `FirstMidName`, `HireDate?`, `OfficeAssignmentLocation` with identical display attributes. Each declares its own `CreateProjection<Instructor, …>()`. (Verified by direct file read.)
  - Same pattern for `Course` (`Index.Result.Course` / `Details.Model` / `Delete.Command`), `Department`, and `Student`.
  - What *is* shared: domain/EF entities (`Models/*.cs`, `Data/SchoolContext.cs`), infrastructure filters, and **shape-free generics** (`PaginatedList<T>`, `ProjectToListAsync<TDestination>`) — mechanism shared without shape, the direct analogue of ADR-0037's generic-envelope carve-out.
  - **Direction of travel:** the older [ContosoUniversityCore](https://github.com/jbogard/ContosoUniversityCore) shared one `MappingProfile.cs` per feature folder; the current repo dissolved it into per-slice profiles. The codebase moved *toward* less cross-slice sharing.
- **Extrapolation, flagged.** He never discusses a hand-rolled, never-serialized `Expression<Func<T, TRow>>` record. His destination types are simultaneously the response the page renders — the wire shape *and* the query shape in one type. Ours separates those (ADR-0037 owns the wire). Applying his DTO rule to an internal projection target is an inference, not a citation.
- **Granularity caveat.** His "slice" is one request. If the queries here are each their own endpoint, his code duplicates without hesitation. If they were multiple reads *inside* one slice, "maximize coupling in a slice" makes sharing correct and the question dissolves. Nothing he wrote resolves the middle case.

## 2. Greg Young and Udi Dahan — CQRS read models

### Udi Dahan

- "One table for each view." — Udi Dahan, [Clarified CQRS](https://udidahan.com/2009/12/09/clarified-cqrs/), § *Queries*.
- **The surrounding text makes the referent unambiguous — it is a storage schema, not a C# type.** The same paragraph proposes creating an *additional data store* whose data may be out of sync with the master database, and has the client issue `SELECT * FROM MyViewTable` and bind the result to the screen. § *Query Data Storage* then asks whether that store even needs to be relational.
- **Its update path is explicit:** § *Data modifications* prescribes an autonomous component that consumes events and updates the query data store, and recommends **one event handler per view model class (per table)**.
- He nowhere discusses an in-process, query-shaping projection that is not separately stored.

### Greg Young

- **The "Thin Read Layer" is the in-process artifact, and it is his recommendation.** After applying CQRS he bypasses the domain for reads: "This layer reads directly from the database and projects DTOs" — Greg Young, [CQRS Documents](https://cqrs.wordpress.com/wp-content/uploads/2010/11/cqrs_documents.pdf) (Nov 2010), § *The Query Side*, p. 21. He explicitly allows it to be tied to the database vendor and to use stored procedures.
- **A separate denormalized store is a later, optional step — not part of the pattern.** Only *after* the Thin Read Layer does he raise whether reads and writes should share one data model, and he states it is feasible to have the read side still use the domain (§ *The Command Side*, p. 23). The two-data-source picture (Figure 12) follows as an option that events then integrate.
- **On per-screen shaping — and an explicit exception permitting sharing.** § *The Query Side* (p. 20) states that DTOs are optimally built to match the client's screens to prevent multiple round trips, and then, in the very next sentence, that with many clients it may be better to build **a canonical model that all of the clients use**.
- **He later scoped the pattern down hard.** "CQRS is a small tactical pattern" — Greg Young, [CQRS](https://gregfyoung.wordpress.com/2012/03/02/cqrs/), in a list that also states CQRS is not Event Sourcing and does not require a message bus. And "CQRS is not an architecture." — Greg Young, [CQRS is not an Architecture](https://gregfyoung.wordpress.com/2012/09/09/cqrs-is-not-an-architecture/).

### Negative findings — claims that do not survive checking

- **"A read model per screen" is not a sentence in the CQRS Documents.** Full-text search of the PDF for `for each screen`, `per screen`, `each view`, `per view`, and `one table` returns nothing of the sort. The nearest statement is the softer "DTOs are optimally built…" above, which is immediately qualified by the canonical-model exception. A summarizer initially produced "you will have a read model for each screen" as a quotation; that string does not occur in the document.
- The document's only uses of "read model" as a maintained store (pp. 51–52) describe **event handlers updating it** — i.e. the persisted kind.

## 3. DRY, the Rule of Three, and the wrong abstraction

- **DRY, correctly attributed.** "Every piece of knowledge must have a single, unambiguous, authoritative representation within a system." — Andy Hunt & Dave Thomas, *The Pragmatic Programmer, 20th Anniversary Edition*, Topic 9 "The Evils of Duplication", Tip 15. Publisher-hosted excerpt: [media.pragprog.com/titles/tpp20/dry.pdf](https://media.pragprog.com/titles/tpp20/dry.pdf). **It is not Fowler's**, and Fowler himself credits *The Pragmatic Programmer* in footnote 2 of BeckDesignRules.
- **The authors' own carve-out is the closest primary analogue to this question.** That same excerpt carries a section headed *Not All Code Duplication is Knowledge Duplication*, whose worked example is two validation functions with byte-identical bodies. A code reviewer calls it a DRY violation; the authors state the reviewer is wrong — the code is the same but the knowledge differs, and two things that merely happen to share rules are a coincidence rather than a duplication.
- **Thomas's own clarification.** "DRY is not about code duplication; it's about the representation of knowledge." — Dave Thomas, [Premature Design Is Not Design](https://articles.pragdave.me/p/premature-design-is-not-design), § "The Devil Likes DRY". He states he rewrote the DRY material for the 20th-anniversary edition because readers were reducing it to "don't copy-paste".
- **Sandi Metz.** "duplication is far cheaper than the wrong abstraction" — Sandi Metz, [The Wrong Abstraction](https://sandimetz.com/blog/2016/1/20/the-wrong-abstraction) (2016), restating an assertion from her RailsConf 2014 talk *All the Little Things*. Her mechanism is a numbered sequence: an abstraction is extracted; a near-miss requirement arrives; the next programmer feels honour-bound to keep it and **adds a parameter plus a conditional**; repeat. Her stated diagnostic is that passing parameters and adding conditional paths through shared code means the abstraction is already incorrect. Her remedy is to inline it back into every caller, let each caller simplify, and only then re-extract.
- **Fowler does not agree, and this should not be smoothed over.** "I find that is rare and easy to spot" — Martin Fowler, [Avoiding Repetition](https://www.martinfowler.com/ieeeSoftware/repetition.pdf), *IEEE Software*, Jan/Feb 2001 — said of duplication that is merely coincidental. In the same column, his prescribed treatment for blocks that are similar but not identical is to **parameterize the varying data** — precisely the move Metz identifies as the failure mode. Fowler (2001) treats coincidental duplication as the rare case; Thomas (2020) and Metz (2016) treat mistaking coincidence for duplication as the common failure.
- **Fowler on Beck's rules.** "The 'no duplication' is perhaps the most powerfully subtle of these rules." — Martin Fowler, [Beck Design Rules](https://martinfowler.com/bliki/BeckDesignRules.html). He notes the tension with "reveals intention" and regards their relative order as unimportant.
- **The operative test, stated first-party.** "changing one element necessitates changing the other element" — Kent Beck, [Coupling](https://tidyfirst.substack.com/p/coupling), defining when two elements are coupled with respect to a change. This is Thomas's acid test in different words.
- **Rule of Three — not verifiable from a free primary source.** `martinfowler.com/bliki/RuleOfThree.html`, `DuplicatedCode.html`, and `AbstractionInversion.html` all return **404**; there is no Fowler bliki entry on duplication or the Rule of Three, and refactoring.com's online catalog omits the Motivation prose. The rule and its credit to **Don Roberts** live in *Refactoring* 2nd ed., Ch. 2, § "When Should We Refactor?" — book-only. Verify wording against a copy before citing it. ([Book page](https://martinfowler.com/books/refactoring.html).)

## 4. The crux: is "read model per view" about a persisted store or an in-process DTO?

**It is about a separately-persisted, denormalized store with its own update path. It does not govern an in-process EF projection type.** The evidence is direct, not inferred:

- Dahan's "one table for each view" sits inside a proposal to create an **additional data store**, is queried with `SELECT *` against a named table, and is kept current by **one event handler per table**. Every load-bearing noun is a storage noun. An in-process C# record has no table, no event handler, and no staleness window.
- Young's per-screen sentence is about **DTO shaping**, not about a stored model — and he qualifies it in the following sentence by permitting **one canonical model shared by all clients** when there are many. His document contains no "one read model per screen" rule.
- The strongest point, and the one that inverts the usual assumption: **`.Select(InvoiceRow.Projection)` *is* Young's Thin Read Layer.** It reads directly from the data model and projects DTOs, bypassing the domain — exactly what he prescribes at that stage. The separate denormalized store is the *next*, optional move in his narrative, and he says outright it is feasible to keep the read side on the domain. His later posts confirm the reduction: CQRS is "a small tactical pattern", not the read-store architecture.

**Consequences for the question as asked:**

- The CQRS literature does **not** supply an argument for duplicating `InvoiceRow` per query. The guidance people invoke for that is aimed at a different artifact, one layer down. Citing "read model per screen" against a shared EF projection target is a category error.
- On the in-process artifact, the nearest primary statement — Young's canonical-model exception — leans **toward permitting sharing** when multiple consumers want the same shape.
- The decision therefore falls to the DRY/coupling layer, where the test is **not** "do these queries return the same columns today" but Beck's and Thomas's shared question: **when one query's column needs change, is the other's forced to change too?**
  - **No** → the shapes coincide by accident. That is Hunt & Thomas's identical-validators case: a coincidence, not a duplication. Duplicate per slice; Bogard's code is the worked precedent.
  - **Yes** → it is one piece of knowledge with two call sites. Keep it shared; Fowler's column is the citation.
- **Metz is a tripwire, not a prohibition.** Her failure mode is *triggered* by parameter and conditional accretion (step 7 of her sequence). A shared row type carrying no mode flag and no caller-keyed conditional is not yet what she describes. Invoking her pre-emptively is a forecast, not a citation — but the moment a `bool includeLines` or a mode enum appears on `InvoiceRow.Projection`, her diagnostic fires and the remedy is to inline back into each handler.

## 5. What this does and does not settle

**Settles:**

- The persisted-vs-in-process crux, with direct textual evidence rather than inference. "Read model per view" is storage guidance.
- Attribution: DRY is Hunt & Thomas, not Fowler; the Rule of Three is Fowler-authored but credits Don Roberts and is not on the free web.
- That Bogard both *states* a per-request DTO rule and *practises* duplication of field-identical projection types across slices — the clearest authority-plus-evidence pair available for the question.
- That the authorities do **not** speak with one voice: Fowler's 2001 position is materially different from Metz's 2016 and Thomas's 2020 positions on how often duplication is coincidental.

**Does not settle:**

- **No primary source addresses this exact artifact.** An internal, never-serialized EF `Expression<Func<T, TRow>>` projection target postdates most of this literature. Bogard's AutoMapper guideline is the nearest on-point rule, and it is about types that are simultaneously the wire response. Everything beyond that is extrapolation and is labelled as such above.
- **Whether the repo's queries pass the coupling test.** That is a per-aggregate judgment about requirements, not a literature question. `GetInvoiceById` and `GetInvoiceByOrderId` returning one shape may well be one piece of knowledge; `GetInvoicesByBuyer` (a list) sharing a detail row is the weaker case, and is the same list-vs-detail divergence ADR-0021 already called out for Ordering.
- **Whether ADR-0021's current permission should change.** This note supplies the tests and the citations; it does not re-decide the ADR.
- The exact *Refactoring* wording for the Rule of Three, which needs a copy of the book.

## Sources

| Source | URL |
|---|---|
| Bogard, *Vertical Slice Architecture* | https://www.jimmybogard.com/vertical-slice-architecture/ |
| Bogard, *AutoMapper Usage Guidelines* | https://www.jimmybogard.com/automapper-usage-guidelines/ |
| Bogard, ContosoUniversityDotNetCore-Pages | https://github.com/jbogard/ContosoUniversityDotNetCore-Pages |
| Bogard, ContosoUniversityCore | https://github.com/jbogard/ContosoUniversityCore |
| Young, *CQRS Documents* (Nov 2010) | https://cqrs.wordpress.com/wp-content/uploads/2010/11/cqrs_documents.pdf |
| Young, *CQRS* | https://gregfyoung.wordpress.com/2012/03/02/cqrs/ |
| Young, *CQRS is not an Architecture* | https://gregfyoung.wordpress.com/2012/09/09/cqrs-is-not-an-architecture/ |
| Dahan, *Clarified CQRS* | https://udidahan.com/2009/12/09/clarified-cqrs/ |
| Hunt & Thomas, *The Pragmatic Programmer* 20th ed., Topic 9 | https://media.pragprog.com/titles/tpp20/dry.pdf |
| Thomas, *Premature Design Is Not Design* | https://articles.pragdave.me/p/premature-design-is-not-design |
| Metz, *The Wrong Abstraction* | https://sandimetz.com/blog/2016/1/20/the-wrong-abstraction |
| Fowler, *Avoiding Repetition* (IEEE Software, 2001) | https://www.martinfowler.com/ieeeSoftware/repetition.pdf |
| Fowler, *Beck Design Rules* | https://martinfowler.com/bliki/BeckDesignRules.html |
| Beck, *Coupling* | https://tidyfirst.substack.com/p/coupling |

Unreachable or non-existent, checked: `martinfowler.com/bliki/RuleOfThree.html`, `DuplicatedCode.html`, `AbstractionInversion.html`, `martinfowler.com/tags/duplication.html` — all 404. `jimmybogard.com/cqrs-mediatr-implementation-patterns/` — 404. The MediatR wiki contains nothing on type placement or sharing. *Refactoring* 2nd ed. Ch. 2 is book-only (not paywalled online — simply not published on the free web).

## Related

- [ADR-0021](../adr/0021-read-side-no-specifications.md) — owns the `*Row` projection-target decision; currently permits sharing across queries of one aggregate.
- [ADR-0037](../adr/0037-endpoint-owned-response-contracts.md) — owns the published wire contract; explicitly scopes `*Row` types out.
- [ADR-0036](../adr/0036-shared-kernel-value-objects.md) — the domain-layer share/duplicate line.
