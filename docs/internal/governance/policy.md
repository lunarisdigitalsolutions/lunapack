# Maintainer Policy

Dependencies use exact versions with committed lock files. A new dependency
needs a clear requirement, an approved permissive license, active maintenance,
target-framework compatibility, and review of its direct and transitive risk.

Pack references also use exact semantic versions. Pack manifests cannot choose
sources; consumer configuration selects the local or Git source, and lock state
records the selected provenance for review.

Each maintained pack has an accountable owner, technical contact, semantic
version, changelog, and release evidence. Pack content should minimize
executable behavior and avoid sensitive values. Security-sensitive packs may
require stricter review.

Documentation is audience-owned and concise. Use Diataxis for reader guidance;
keep current implementation guidance separate from product planning. Keep
developer, internal, and product links within their own audience area.

LunaPack remains free and open source. The repository does not use a GitHub
funding configuration; commercial consulting and training are offered by
[Lunaris Digital Solutions](https://lunaris.digital) and are separate from
access to the software and maintained packs.

Release archives and packages must retain LunaPack's license and include
third-party notices required by redistributed dependencies. Maintainers review
direct and transitive licenses whenever dependencies change. Generated build,
test, website, package-staging, and scan output stays untracked unless it is an
intentional release asset or canonical generated contract reviewed in source.
