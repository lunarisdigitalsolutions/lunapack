# Project document reference

Pack authors need to understand the documents created in a consumer project.

| Document            | Meaning for a pack author                                                                                      |
| ------------------- | -------------------------------------------------------------------------------------------------------------- |
| `lunapack.yml`      | Holds selected sources, direct root requests, optional destinations, and reusable string or Boolean variables. |
| `lunapack-lock.yml` | Holds the resolved graph, effective targets, provenance, and digests. LunaPack owns this document.             |
| `pack.yml`          | Defines the reusable pack and is validated when a source is discovered.                                        |

A direct consumer request can relocate only that pack's files with a
project-relative destination. Dependency targets keep their manifest targets.
Explicit parameter values are not stored in project state. A matching project
variable can supply a compatible value during installation.
