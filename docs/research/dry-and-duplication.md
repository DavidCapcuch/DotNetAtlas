# Detecting and unwinding a wrong abstraction

Research note (2026-07-28). Primary sources only — author-owned blogs and wikis, their own talks and papers, publisher-hosted book text. Every quotation was verified against raw downloaded page text or `pdftotext` output, never against a page-summarizer's rendering; a summarizer in this session fabricated a plausible attribution, so none were trusted.

Companion to [read-model-sharing.md](read-model-sharing.md), which settles **what DRY claims** and **the forward test** (when one site changes, must the other?) with verified citations for Hunt & Thomas, Dave Thomas, Metz's cost assertion, Fowler-2001, Beck's coupling definition, and the Rule of Three. None of that is repeated here.

## The question

That note answers *should these two things be shared*. This one answers the two questions it left open:

- **Detection** — the sharing already happened, possibly years ago. What are the named, citable symptoms that it was wrong?
- **Unwinding** — once diagnosed, what is the prescribed procedure for backing out, and when does that procedure stop being available?

## 1. Detection symptoms

Framing, first-party: a smell is a surface indication that usually corresponds to a deeper problem, and "smells aren't inherently bad on their own" — Martin Fowler, [Code Smell](https://martinfowler.com/bliki/CodeSmell.html) (he credits Kent Beck with coining the term). **Every symptom below is an indicator, not a verdict.** Each still has to be checked against the forward test in the companion note.

| # | Symptom | Status |
|---|---|---|
| 1 | Parameter + conditional accretion | **Cited** — Metz |
| 2 | Boolean / enum mode flag on a shared entry point | **Cited** — Fowler |
| 3 | Special cases arriving after the merge, then being pushed back out to callers | **Cited** — Abramov |
| 4 | Callers depending on only a subset of the shape | **Cited** — Martin (ISP), by inference to types |
| 5 | Lowest-common-denominator / must-handle-everything shape | **Cited** — Winters et al. |
| 6 | Tests written against the shared code rather than the callers | **Cited** — Abramov |
| 7 | Nobody can say what the abstraction represents | **Cited** — Abramov |
| 8 | Shared code needing per-caller test setup | **Uncited** — no first-party source found |

### 1.1 Parameter and conditional accretion — the canonical diagnostic

- Metz states it as a test on your own behaviour, not on the code's appearance: "passing parameters and adding conditional paths through shared code, the abstraction is incorrect" — Sandi Metz, [The Wrong Abstraction](https://sandimetz.com/blog/2016/1/20/the-wrong-abstraction).
- Her numbered sequence (steps 1–8, verified against the post) is a *history*, not a shape: extract → time passes → an almost-perfect requirement → Programmer B feels honour-bound to keep the abstraction → parameter + conditional → repeat. The named driver is the **sunk cost fallacy**, which she calls out explicitly.
- Sharpest consequence for review: she states the abstraction may have been correct originally and has since stopped being so. **Wrongness is a property of the current requirement set, not of the original extraction.** Blaming the person who extracted it is a category error.

### 1.2 Boolean / enum mode flags

- The strongest independently-citable version of symptom 1, and it predates Metz's post by five years: a flag argument is one that tells the function to carry out a different operation depending on its value. "My general reaction to flag arguments is to avoid them." — Martin Fowler, [Flag Argument](https://martinfowler.com/bliki/FlagArgument.html) (2011). His stated reason is caller intent: `regularBook(martin)` reads; `book(martin, false)` does not.
- **Fowler carves out an exception, and it materially weakens the "flag ⇒ inline it" reading.** Where the two branches are *tangled* — interleaved shared and branch-specific steps — he says extracting two separate methods causes significant duplication, and instead recommends **keeping** the flag-argument method but hiding it behind two named public entry points, with a deliberately ugly private name so nothing else calls it. So: the flag on a *published* signature is the smell; the flag on a *hidden implementation* reached only through intention-revealing wrappers is his prescribed treatment.
- Second carve-out — **deriving the flag**. If the branch depends only on the state of an argument the caller already holds (his example: customer status), the caller has no business specifying it and the routine should derive it. His rule: you only want separate methods when the caller needs to choose.
- Third, and it cuts against reflexive flag-removal: if the caller is piping a boolean straight from a UI control or data source, the flag argument is justified — the API should be written to make it easier for the caller.
- Named transformations, first-party catalog: [Remove Flag Argument](https://refactoring.com/catalog/removeFlagArgument.html) (alias *Replace Parameter with Explicit Methods*) and its inverse [Parameterize Function](https://refactoring.com/catalog/parameterizeFunction.html). The free catalog carries only signature-level sketches — the Motivation prose is book-only.

### 1.3 The post-merge special-case sequence

- Dan Abramov's independently-derived version of Metz's sequence, from the verbatim transcript of his own talk: same shape but async vs sync → unify rather than copy → a bug, because the two cases were only *nearly* the same → add an `if` special case → discover the original case had the mirror-image bug → add another → the abstraction now looks intimidating, so **make it more generic**: hoist the special cases back out into the callers and parametrize everything. His stated end state is that the surviving abstraction no longer means anything to anyone. — Dan Abramov, [The WET Codebase](https://www.deconstructconf.com/2019/dan-abramov-the-wet-codebase) (Deconstruct 2019), linked from his own [overreacted.io](https://overreacted.io/the-wet-codebase/).
- **The generalizing step is the one worth flagging in review.** Removing caller-specific conditionals *from* shared code by pushing them back *to* callers looks like a cleanup and reads well in a diff. It is step 5 of the decay, not a repair: the abstraction that survives is shape without meaning.
- His stated reason nobody stops it: the progression is gradual enough that every individual step looks reasonable to the people writing and reviewing that commit — which means **per-commit review cannot see this smell.** It is only visible over the history of the shared file. *(Extrapolation: that implies detection belongs in a periodic sweep over churn history, not in PR review. No source says this.)*

### 1.4 Callers using only a subset of the shape

- First-party statement of the principle, though about interfaces rather than data shapes — Martin's one-line form of the **Interface Segregation Principle** is that interfaces should be fine-grained and client-specific: Robert C. Martin, [The Principles of OOD](http://butunclebob.com/ArticleS.UncleBob.PrinciplesOfOod) (his own wiki).
- **Flagged as an inference:** ISP is written about method sets a client is forced to depend on. Applying it to a *record's fields* — a projection row where one query never reads three of the columns — is an extension of the principle, not a citation of it. The companion note's forward test is the load-bearing check; ISP is corroboration.
- Martin's package-level companion is more directly on point and is quoted in §4 below.

### 1.5 Lowest-common-denominator shapes

- First-party and unusually blunt about the trade: a bespoke solution customized for a narrow problem space may outperform a general utility solution that has to handle every possibility — Winters, Manshreck & Wright, [*Software Engineering at Google*](https://abseil.io/resources/swe-book/html/ch01.html), Ch. 1 (free HTML edition). They also name the second benefit of forking: it isolates you from changes dictated by another team or a third party.
- The same passage immediately argues the other way, and that half is in §3.

### 1.6 Tests as the lock-in mechanism

- The symptom nobody expects: **a green, well-covered shared abstraction is harder to unwind than an untested one.** Abramov's argument, from the same transcript: the intuitive place to put unit tests is on the abstraction, because that is where the complex code is. He calls that a bad idea — if you later inline the abstraction, every one of those tests fails, and the social pressure against being the person who lowered code coverage reverts the fix.
- His prescription is to test **code with concrete business value** — the callers, the features — so the tests are indifferent to whether the shared code exists, and will actively confirm the inline was behaviour-preserving.
- **This is the single most actionable detection/prevention item in the note**, and it aligns with the repo's own testing bar (observable outcomes over structure, resistance-to-refactoring as a first-class property). Tests pinned to the shared type *are* the change-detector anti-pattern, arriving by a route that looks responsible.
- Abramov names a second lock-in with no clean answer: if the shared code holds **mutable shared state**, inlining duplicates that state and the rewiring may not be feasible. He states outright he has no good solution.

### 1.7 The symptom that is folklore

- **"Shared code whose test suite needs per-caller setup"** — no first-party source found asserting this. The nearest primary material is Jay Fields arguing *against* DRY in test setup generally ([Testing: Duplicate Code in Your Tests](http://blog.jayfields.com/2008/05/testing-duplicate-code-in-your-tests.html), 2008), which is about test readability, not about diagnosing the production abstraction under test. Treat the symptom as a plausible heuristic with no citation behind it.

## 2. The unwinding procedure

### 2.1 Metz's remedy, in full

Her three steps, in her order (paraphrased; her framing throughout is that the fastest way forward is back):

1. **Re-introduce the duplication** by inlining the abstracted code back into every caller. Every caller — not just the awkward ones.
2. **Use each caller's passed parameter values to identify** which subset of the inlined code that specific caller actually executes. The parameter values are the input to the simplification, which is why step 1 must precede any parameter removal.
3. **Delete the rest** in each caller.

Points about the procedure that are easy to lose in paraphrase and are stated in her text:

- It removes **both** the abstraction and the conditionals. Deleting a flag while keeping the shared body is not this procedure.
- Her stated expectation of the outcome: it is *common* to find that although each caller ostensibly invoked a shared abstraction, the code each was actually running was fairly unique. **The inline is diagnostic** — it tells you how much was ever really shared.
- Re-extraction is explicitly permitted, but only **after** the old abstraction is completely removed — re-isolate the duplication and re-extract from scratch.
- She permits accumulating **a few** conditionals deliberately to gain insight, while warning the pain grows the longer you wait.
- The blocker she names is not technical. It is sunk cost — and specifically that the *more* incomprehensible the code, the *stronger* the pull to preserve it.

### 2.2 Abramov's version, and what it adds

- Same core move, independently: his advice to his past self is to inline the abstraction, meaning literally copy the code back to the places that use it, accepting the duplication in order to destroy the thing being created.
- He adds a **social** precondition Metz does not: it must be culturally acceptable on the team to say an abstraction is bad and should be deleted. His stated reason — a new engineer will not volunteer to be the person proposing copy-paste. He frames deleting abstractions as a normal part of a healthy process, not an admission of failure.
- He also adds the honest limit: **the window closes.** Once other teams consume the shared code and you no longer know how to verify their usage — worse, once the owning team has been reorged out of existence — the inline cannot be performed even with full agreement that it should be. This is the same boundary Fowler draws in §4, reached from the opposite direction.

### 2.3 Named transformations

Fowler's catalog names the mechanical steps; the free online catalog gives signatures only, the rationale is book-only.

- [Inline Function](https://refactoring.com/catalog/inlineFunction.html) (alias *Inline Method*), explicitly the inverse of Extract Function — Metz's step 1.
- [Remove Flag Argument](https://refactoring.com/catalog/removeFlagArgument.html) — the §1.2 treatment, where the tangling carve-out does not apply.

### 2.4 Prevention, since detection is late by construction

- **AHA — "Avoid Hasty Abstractions"** — Kent C. Dodds, [AHA Programming](https://kentcdodds.com/blog/aha-programming) (2020). **Attribution correction:** Dodds is routinely credited with coining AHA, including by web summarizers consulted for this note; his own post credits the acronym to **Cher Scarlett** ([the tweet he links](https://x.com/cherthedev/status/1112819136147742720), not publicly fetchable without auth) and credits the underlying idea to Metz's post.
  - **His stated relationship to DRY:** he subscribes to it, but less dogmatically than its canonical wording invites. He opens with duplication costing him personally — one inherited codebase made him fix the same bug in eight places. He is not anti-DRY.
  - **His stated relationship to WET:** he calls it just as dogmatic and over-prescriptive as DRY — i.e. he rejects *both* rules, not just DRY. His own contribution is the added principle *optimize for change first*, and his practical rule is to duplicate until the use cases are known and the commonalities are obvious.
- **Abramov's prevention rules**, same transcript: test the concrete callers (§1.6); restrain the merge impulse, because similar structure may mean you do not understand the problem yet; and prefer dependency shapes (he cites React's one-directional component tree) where inlining is mechanically a copy-paste, so a bad decision stays reversible.

## 3. The cost asymmetry, and the case against it

### 3.1 What supports it

- Metz's cost assertion and Fowler's 2001 disagreement with it are covered in [read-model-sharing.md § 3](read-model-sharing.md) and are not restated.
- The strongest *mechanistic* first-party support is Abramov's cost breakdown, because it names what is being paid rather than asserting a ranking. Costs of a shared abstraction, his terms:
  - **"abstraction creates accidental coupling"** — a bug fix at the shared site makes every other call site your responsibility to re-verify.
  - **Extra indirection** — the promise was that you could reason about one layer; the bug crosses all of them. His name for the result: lasagna code, over-layered rather than tangled.
  - **Inertia** — largely social. Nobody has time to unwind it, and a newcomer will not propose it.
- The concrete-cost account of the same trade, from the person who made the mistake: "My code traded the ability to change requirements for reduced duplication" — Dan Abramov, [Goodbye, Clean Code](https://overreacted.io/goodbye-clean-code/) (2020). The trigger that proved it wrong was **special cases arriving** (§1.3), and his stated counterfactual is that the duplicated version would have absorbed them easily.
- Adjacent, and about speculative rather than premature abstraction: Fowler's yagni cost model decomposes a presumptive feature into **cost of build, cost of delay, cost of carry, cost of repair** — Martin Fowler, [Yagni](https://martinfowler.com/bliki/Yagni.html). *Extrapolation:* an abstraction extracted for anticipated callers is a presumptive feature by his definition, so the model applies; he does not make that application himself.

### 3.2 The strongest opposing case

Do not read the above as consensus. Four first-party lines of argument push the other way, and two of them come from authors quoted above.

- **Duplication's failure mode is silent and unbounded.** Dodds' eight-places bug and Abramov's own statement of the pro-extraction argument — you fix the bug in one copy, the other stays broken because you forgot it exists — are both first-party, from the two loudest advocates of duplication. A wrong abstraction announces itself (the conditionals pile up in one file); a missed copy does not announce itself at all.
- **At organizational scale, Google inverts the recommendation.** *Software Engineering at Google* endorses a **One-Version Rule**: developers within an organization must not have a choice of which version of an existing component to depend on, because choice leads to merge strategies, diamond dependencies, lost work, and wasted effort. Their stated cost of the duplication-friendly world is security response: patching a vulnerable library stops being one dependency bump and becomes an exercise in finding every fork and every user of every fork.
- **Their scoping rule is explicit, and it is the useful part**: forks are less risky for short-lived projects and provably narrow scope, but "avoid forks for interfaces that could operate across time or project-time boundaries" — Winters, Manshreck & Wright, [*Software Engineering at Google*](https://abseil.io/resources/swe-book/html/ch01.html), Ch. 1, naming data structures, serialization formats, and networking protocols. See §4.
- **Fowler's flag-argument carve-out** (§1.2) is a narrow but real counter-instance from inside the smell itself: where branches are genuinely tangled, he prefers to retain the shared parameterized implementation over duplicating it.

### 3.3 Verdict

- The asymmetry is **directional, not quantified**. No source in this note offers cost data, a study, or a model that produces a number. Metz's claim is an assertion from experience, restated from a talk; Abramov's is a narrative; Google's counter is also experiential, differing mainly in the scale it was formed at.
- The genuine, non-smoothed disagreement is about **scale and boundary**, not about mechanism. Metz, Abramov, Dodds all argue from *one team's code that one team can change*. Google argues from *thousands of engineers and a security-patch obligation across forks*. Fowler-2001 (companion note) sits with Google on frequency.
- **The reconciliation the sources jointly support** — flagged as this note's synthesis, not any one author's claim: the asymmetry holds where the unwinding remedy is available, and reverses where it is not. That boundary is §4.

## 4. The contract boundary, where extract-later expires

Metz's remedy has an unstated precondition: **you can reach every caller.** Once you cannot, steps 1–3 are unexecutable and the whole cost calculus changes. That precondition has first-party names.

- **Fowler's public/published distinction is exactly this precondition.** "anything published so you can't reach the calling code needs more complicated treatment" — Martin Fowler, [Published Interface](https://martinfowler.com/bliki/PublishedInterface.html) (2003), a term he says he first used in *Refactoring*.
- The full argument is in his IEEE column, and the pivot sentence is the availability of the remedy, not visibility: the key difference is "being able to find and change the code that uses an interface" — Martin Fowler, [Public versus Published Interfaces](https://martinfowler.com/ieeeSoftware/published.pdf), *IEEE Software*, Mar/Apr 2002. He argues the published/public distinction matters more than public/private. His advice there:
  - Don't treat an interface as published if you *can* find and change all its users — just make the change.
  - **Publish as little as you can, as late as you can.** Keep published interfaces thin.
  - Recast changes as **additions** where possible, since additions don't break existing clients — with the caveat that even additions break outside parties who *implement* your interface.
  - Don't publish inside a team; strong code ownership forces interperson interfaces to behave as published ones, which he treats as a cost of that ownership model.
- **Why publication is irreversible rather than merely inconvenient.** "all observable behaviors of your system will be depended on by somebody" — Hyrum Wright, [Hyrum's Law](https://www.hyrumslaw.com/) (his own site; he credits **Titus Winters** with naming it). His stronger corollary, which he calls the *Law of Implicit Interfaces*: given enough consumers, the implicit interface converges on the implementation, and the interface has effectively evaporated. *Software Engineering at Google* Ch. 1 treats this as a dominant factor in any discussion of changing software over time and compares it to entropy — mitigable, never eradicable.
- **The package-level statement of the same rule**, and the tersest formulation in this note: "The granule of reuse is the granule of release." — Robert C. Martin, REP, [The Principles of OOD](http://butunclebob.com/ArticleS.UncleBob.PrinciplesOfOod). Sharing a type across a release boundary makes that type a released artifact carrying the release's cadence. His **Common Closure Principle** on the same page — package together the classes that change together — is the companion note's forward test raised to the packaging level.
- **The counter-pressure, and it is real.** Google's rule against forking *interfaces that cross boundaries* — data structures, serialization formats, networking protocols — is not the same claim as "duplicate your DTOs at the boundary". Their concern is two parties forking a format they must both understand, then drifting until they cannot interoperate. *Extrapolation, flagged:* that is orthogonal to a producer keeping separate response types per endpoint while still publishing one authoritative schema per endpoint — one definition per contract is preserved either way. No source addresses that configuration directly, and reading Google's line as endorsing either side of it would be overreach.

## 5. AHA, WET, moist, and DAMP — what is actually attributable

Asked for explicitly, because these four circulate as if they were peers. They are not.

| Term | First-party origin | Verdict |
|---|---|---|
| **AHA** — "Avoid Hasty Abstractions" | **Cher Scarlett**, credited by Dodds in [AHA Programming](https://kentcdodds.com/blog/aha-programming); the essay is Dodds' | Attributable; commonly **mis**attributed to Dodds |
| **WET** — definition | Conlin Durbin: "You can ask yourself \"Haven't I written this before?\" two times, but never three." ([post](https://dev.to/wuz/stop-trying-to-be-so-dry-instead-write-everything-twice-wet-5g33), cited by Dodds) | The *definition* is attributable; the acronym is not |
| **WET** — acronym | None found | **Folklore.** Abramov, needing its meaning, falls back on Wikipedia, which lists four competing expansions — write every time / write everything twice / we enjoy typing / waste everyone's time. Durbin proposes his definition; he does not claim the acronym |
| **MOIST** | None found | **Folklore.** In the sources checked, "moist" appears only in a reader comment on Durbin's post. No first-party coinage located |
| **DAMP** — "Descriptive And Meaningful Phrases" | **Jay Fields**, [DRY code, DAMP DSLs](http://blog.jayfields.com/2006/05/dry-code-damp-dsls.html) (2006) | Attributable — **but about DSLs, not tests** |

The DAMP finding is worth stating precisely, because the common form of the claim is not what the source says:

- Fields coined DAMP to answer whether DRY applies to **domain-specific languages**. His answer is no: his poker-room DSL keeps filler words (*the*, *list*, *is*, *than*, *to*) as no-op "bubble" methods so business users can read the rule as a sentence, and stripping them to the minimum token set loses the meaning even though it removes the redundancy. His stated bar is that a DSL requiring training to understand has room for improvement.
- **He never applies DAMP to test code in that post.** He *does* argue at length against DRY in tests two years later — setup and teardown as a deodorant that hides a design problem rather than fixing it, the cost of having to look in three places when a test breaks, the observation that each test is its own context — but that post never uses the acronym. ([Testing: Duplicate Code in Your Tests](http://blog.jayfields.com/2008/05/testing-duplicate-code-in-your-tests.html), 2008.)
- **Verdict: "DAMP over DRY in tests" is a community synthesis of two separate Fields posts.** Both halves trace to him; the pairing does not. Cite the 2008 post for the test argument and stop calling it DAMP, or call it DAMP and cite 2006 for the phrase only.

## 6. Fowler smell entries — 404 report

Checked directly (HTTP status from a raw fetch, not a search engine):

| URL | Status |
|---|---|
| `martinfowler.com/bliki/SpeculativeGenerality.html` | **404** |
| `martinfowler.com/bliki/DivergentChange.html` | **404** |
| `martinfowler.com/bliki/ShotgunSurgery.html` | **404** |

- **Speculative Generality, Divergent Change, and Shotgun Surgery are book-only** — *Refactoring* Ch. 3 (2nd ed.), which Fowler co-authored with Kent Beck. There is no bliki entry for any of them, consistent with the companion note's finding that `RuleOfThree.html` and `DuplicatedCode.html` are also 404.
- The reachable bliki substitutes, all verified 200 and used above: [Code Smell](https://martinfowler.com/bliki/CodeSmell.html) (the concept, credited to Beck), [Flag Argument](https://martinfowler.com/bliki/FlagArgument.html) (the closest free entry to a duplication smell), [Yagni](https://martinfowler.com/bliki/Yagni.html) (the cost model for speculative work), [Published Interface](https://martinfowler.com/bliki/PublishedInterface.html).
- **Do not cite the three smells to a URL.** Either cite the book with a chapter, or use the free substitutes.

## 7. What this settles and does not settle

**Settles:**

- **Metz's remedy is three ordered steps, and the order is load-bearing** — inline everywhere first, *then* use the passed parameter values to pick each caller's subset, *then* delete. Removing a flag while keeping the shared body is not her procedure.
- **Two independent derivations of the same decay sequence** — Metz (2016) and Abramov (2019) reach nearly identical step lists from different languages and ecosystems, and Abramov cites her. Independent enough to be worth more than one anecdote; not independent enough to call corroboration.
- **The make-it-more-generic move is part of the decay, not a repair** (§1.3) — the one detection finding that contradicts how the move reads in a diff.
- **Tests on the shared abstraction are a lock-in mechanism** (§1.6), with a first-party prescription: test the callers instead.
- **Flag arguments have a first-party carve-out** — Fowler's tangled-implementation case, which permits retaining a hidden parameterized implementation behind named entry points.
- **The unwinding window closes at the publication boundary**, named independently by Fowler (can't reach the callers), Wright (consumers depend on everything observable), Martin (granule of reuse = granule of release), and Abramov (can't verify other teams' usage).
- **Attribution:** AHA is Cher Scarlett's, not Dodds'; DAMP is Fields' but was coined for DSLs; WET's acronym and MOIST are folklore with no locatable first-party origin.
- **Three Fowler smell URLs are 404** and the smells are book-only.

**Does not settle:**

- **The cost asymmetry is unquantified in every source found.** Metz asserts it; Abramov narrates it; Google's One-Version Rule argues the reverse at their scale. Nobody produces a measurement. Anyone citing "duplication is cheaper" as settled is overstating what the sources support.
- **Where the scale threshold sits** between the one-team regime (asymmetry holds) and the many-team regime (Google's inversion). Both sides argue from experience at their own scale; neither states a crossover.
- **Symptom 8** (shared code needing per-caller test setup) has no first-party source and should not be presented as though it does.
- **How Google's don't-fork-the-format rule interacts with per-endpoint response types** — §4's final bullet; treated as orthogonal, flagged as extrapolation, unaddressed by any source.
- **Whether any of this changes a repo decision.** This note supplies symptoms, a procedure, and a boundary. It does not re-decide [ADR-0021](../adr/0021-read-side-no-specifications.md) or [ADR-0037](../adr/0037-endpoint-owned-response-contracts.md).
- The book-only material — *Refactoring* Ch. 3 smells, the Motivation prose behind Remove Flag Argument and Inline Function — needs a copy of the book to quote.

## Sources

| Source | URL |
|---|---|
| Metz, *The Wrong Abstraction* | https://sandimetz.com/blog/2016/1/20/the-wrong-abstraction |
| Abramov, *The WET Codebase* (Deconstruct 2019, verbatim transcript) | https://www.deconstructconf.com/2019/dan-abramov-the-wet-codebase |
| Abramov, *The WET Codebase* (his own post linking the talk) | https://overreacted.io/the-wet-codebase/ |
| Abramov, *Goodbye, Clean Code* | https://overreacted.io/goodbye-clean-code/ |
| Dodds, *AHA Programming* | https://kentcdodds.com/blog/aha-programming |
| Durbin, *Stop trying to be so DRY…* (WET definition, cited by Dodds) | https://dev.to/wuz/stop-trying-to-be-so-dry-instead-write-everything-twice-wet-5g33 |
| Fields, *DRY code, DAMP DSLs* | http://blog.jayfields.com/2006/05/dry-code-damp-dsls.html |
| Fields, *Testing: Duplicate Code in Your Tests* | http://blog.jayfields.com/2008/05/testing-duplicate-code-in-your-tests.html |
| Fowler, *Flag Argument* | https://martinfowler.com/bliki/FlagArgument.html |
| Fowler, *Code Smell* | https://martinfowler.com/bliki/CodeSmell.html |
| Fowler, *Yagni* | https://martinfowler.com/bliki/Yagni.html |
| Fowler, *Published Interface* | https://martinfowler.com/bliki/PublishedInterface.html |
| Fowler, *Public versus Published Interfaces* (IEEE Software, 2002) | https://martinfowler.com/ieeeSoftware/published.pdf |
| Fowler, *Inline Function* / *Remove Flag Argument* / *Parameterize Function* | https://refactoring.com/catalog/ |
| Wright, *Hyrum's Law* | https://www.hyrumslaw.com/ |
| Winters, Manshreck & Wright, *Software Engineering at Google*, Ch. 1 | https://abseil.io/resources/swe-book/html/ch01.html |
| Winters, Manshreck & Wright, *Software Engineering at Google*, Ch. 16 (One-Version Rule) | https://abseil.io/resources/swe-book/html/ch16.html |
| Martin, *The Principles of OOD* (ISP, REP, CCP, CRP) | http://butunclebob.com/ArticleS.UncleBob.PrinciplesOfOod |

**Unreachable or non-existent, checked:** `martinfowler.com/bliki/SpeculativeGenerality.html`, `DivergentChange.html`, `ShotgunSurgery.html` — all **404**. Cher Scarlett's originating AHA tweet (`x.com/cherthedev/status/1112819136147742720`) is not fetchable without authentication; it is cited here only as the attribution Dodds himself gives. The archived Object Mentor ISP paper did not download as a readable PDF, so ISP is cited from Martin's own wiki instead.

## Related

- [read-model-sharing.md](read-model-sharing.md) — the companion: what DRY claims, the forward test, Bogard's per-slice evidence, and the Fowler-2001 / Metz-2016 disagreement.
- [ADR-0021](../adr/0021-read-side-no-specifications.md) — owns the `*Row` projection-target decision.
- [ADR-0037](../adr/0037-endpoint-owned-response-contracts.md) — owns the published wire contract; §4 here is the literature behind its boundary.
- [ADR-0036](../adr/0036-shared-kernel-value-objects.md) — the domain-layer share/duplicate line.
