#!/usr/bin/env python3
import os
from functools import lru_cache
from typing import Any

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field
from sentence_transformers import SentenceTransformer
import uvicorn


class EmbeddingRequest(BaseModel):
    input: str | list[str]
    model: str | None = None
    input_type: str = Field(default="document")


def _normalize_input(text: str, input_type: str) -> tuple[str, str | None]:
    normalized_type = (input_type or "document").strip().lower()
    if normalized_type == "query":
        return text, "query"

    return text, None


@lru_cache(maxsize=2)
def load_model(model_name: str) -> SentenceTransformer:
    device = os.getenv("ARA_EMBED_DEVICE", "cpu")
    return SentenceTransformer(model_name, device=device)


def encode_texts(model_name: str, texts: list[str], input_type: str) -> list[list[float]]:
    model = load_model(model_name)
    prepared: list[str] = []
    prompt_name: str | None = None

    for text in texts:
        normalized_text, candidate_prompt_name = _normalize_input(text, input_type)
        prepared.append(normalized_text)
        prompt_name = candidate_prompt_name

    try:
        embeddings = model.encode(
            prepared,
            convert_to_numpy=True,
            normalize_embeddings=False,
            prompt_name=prompt_name,
        )
    except Exception:
        embeddings = model.encode(
            prepared,
            convert_to_numpy=True,
            normalize_embeddings=False,
        )

    return [row.astype("float32").tolist() for row in embeddings]


app = FastAPI(title="ARA Local Embedding Service")


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok"}


@app.post("/embeddings")
def embeddings(request: EmbeddingRequest) -> dict[str, Any]:
    model_name = request.model or os.getenv("ARA_EMBED_MODEL", "Snowflake/snowflake-arctic-embed-m-v1.5")

    try:
        if isinstance(request.input, list):
            vectors = encode_texts(model_name, request.input, request.input_type)
            data = [{"index": index, "embedding": vector} for index, vector in enumerate(vectors)]
            return {"object": "list", "model": model_name, "data": data}

        vector = encode_texts(model_name, [request.input], request.input_type)[0]
        return {"model": model_name, "embedding": vector}
    except Exception as exc:
        raise HTTPException(status_code=500, detail=str(exc)) from exc


if __name__ == "__main__":
    host = os.getenv("ARA_EMBED_HOST", "127.0.0.1")
    port = int(os.getenv("ARA_EMBED_PORT", "8001"))
    uvicorn.run(app, host=host, port=port)
