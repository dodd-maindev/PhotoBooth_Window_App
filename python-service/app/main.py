import logging
from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from fastapi.responses import Response

from .processing import process_image_bytes
from .schemas import FilterType

# Setup logging
logging.basicConfig(
    level=logging.DEBUG,
    format='%(asctime)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

app = FastAPI(title="Photobooth Image Service", version="1.0.0")


@app.get("/health")
async def health() -> dict[str, str]:
    return {"status": "ok"}


@app.post("/process-image")
async def process_image(
    image_file: UploadFile = File(...),
    filter_type: FilterType = Form(...),
) -> Response:
    if not image_file.filename:
        raise HTTPException(status_code=400, detail="Missing image filename")

    try:
        logger.debug(f"Processing request - File: {image_file.filename}, Filter: {filter_type}, Filter type: {type(filter_type)}")
        
        image_bytes = await image_file.read()
        logger.debug(f"Image loaded: {len(image_bytes)} bytes")
        
        if not image_bytes:
            raise HTTPException(status_code=400, detail="Empty image file")

        logger.debug(f"Applying filter '{filter_type}' to image")
        output_bytes = process_image_bytes(image_bytes, filter_type)
        
        logger.debug(f"Filter applied. Output size: {len(output_bytes)} bytes")
        logger.info(f"✓ Successfully processed {image_file.filename} with filter '{filter_type}'")
        
        return Response(content=output_bytes, media_type="image/png")
    except HTTPException:
        raise
    except Exception as exc:
        logger.error(f"Image processing failed: {exc}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Image processing failed: {exc}") from exc