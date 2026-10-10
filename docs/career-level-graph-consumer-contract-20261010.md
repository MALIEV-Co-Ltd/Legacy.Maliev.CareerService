# Career level graph contract

The graph depends on the endpoint. A direct level response from `GET /jobs/Levels`, `GET /jobs/Levels/{id}`, or successful level creation contains the initialized, unloaded `offers: []` collection. That empty navigation is not a count of persisted offers. The ordinary fresh request does not load offers when it queries levels.

`GET /Jobs` includes scalar level metadata in each paginated offer. The original controller clears the related level's offers navigation before serialization, and its null-ignore configuration omits that property. The current nested level DTO has no offers member. That omission is the producer wire contract. The original Web client deserializes into a model whose level constructor initializes an empty offers collection; its own JsonResult can consequently emit `offers: []` and include nullable fields. Consumer handler output must be qualified separately from the producer wire. This source trace does not establish a missing collection in Career Jobs.

Original offer detail uses an unloaded navigation from a fresh context. The current producer includes level metadata in detail responses. This additional data is a current enrichment, not evidence that the original detail response included a level graph. Its broader compatibility disposition remains open; this change does not alter detail responses.

The original Web search handler returns only offer items after fetching offers. It does not depend on a levels request. Full-page and change-item-count handlers fetch levels separately and resolve display names by level ID. Consumer implementations must distinguish those dependencies. This repository does not change the Web consumer.

The new component cases seed two levels and three offers, including siblings on one level. They distinguish unloaded direct navigation from omitted nested navigation, check complete scalar response fields and pagination, and compare all persisted offer/level fields and xmin versions before and after reads. They reuse the existing PostgreSQL/Redis fixture and retain every original test.

Source review is bound to the original checkpoint and accepted Career main recorded in the private migration evidence. No private source or history is copied into this repository. Original SQL Server/SDK execution and genuine IAM/Intranet/consumer acceptance remain open. Local build, tests and formatting are not run without a native allocation; hosted checks on the exact proposed commit are required before protected acceptance.
