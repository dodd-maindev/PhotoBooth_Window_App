from __future__ import annotations

import logging
from io import BytesIO

import cv2
import numpy as np
from PIL import Image, ImageEnhance, ImageFilter

from .schemas import FilterType

logger = logging.getLogger(__name__)


def _pil_to_bytes(image: Image.Image) -> bytes:
    buffer = BytesIO()
    image.save(buffer, format="PNG")
    return buffer.getvalue()


def _add_vignette(bgr_image: np.ndarray) -> np.ndarray:
    logger.debug("Applying vignette effect")
    rows, cols = bgr_image.shape[:2]
    kernel_x = cv2.getGaussianKernel(cols, cols / 2)
    kernel_y = cv2.getGaussianKernel(rows, rows / 2)
    kernel = kernel_y * kernel_x.T
    mask = kernel / kernel.max()
    vignette = np.empty_like(bgr_image)
    for channel in range(3):
        vignette[:, :, channel] = bgr_image[:, :, channel] * mask
    logger.debug("Vignette applied")
    return vignette


def _apply_grayscale(image: Image.Image) -> Image.Image:
    logger.debug("Applying grayscale filter")
    result = image.convert("L").convert("RGB")
    logger.debug("Grayscale filter applied")
    return result


def _apply_blur(image: Image.Image) -> Image.Image:
    logger.debug("Applying blur filter")
    result = image.filter(ImageFilter.GaussianBlur(radius=6))
    logger.debug("Blur filter applied")
    return result


def _apply_vintage(image: Image.Image) -> Image.Image:
    logger.debug("Applying vintage filter")
    rgb = np.array(image.convert("RGB"))
    bgr = cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR)

    sepia_filter = np.array(
        [
            [0.393, 0.769, 0.189],
            [0.349, 0.686, 0.168],
            [0.272, 0.534, 0.131],
        ]
    )
    sepia = cv2.transform(rgb, sepia_filter)
    sepia = np.clip(sepia, 0, 255).astype(np.uint8)
    sepia_bgr = cv2.cvtColor(sepia, cv2.COLOR_RGB2BGR)
    vignette = _add_vignette(sepia_bgr)
    final_rgb = cv2.cvtColor(np.clip(vignette, 0, 255).astype(np.uint8), cv2.COLOR_BGR2RGB)

    output = Image.fromarray(final_rgb)
    output = ImageEnhance.Color(output).enhance(0.82)
    output = ImageEnhance.Contrast(output).enhance(1.08)
    logger.debug("Vintage filter applied")
    return output


def _apply_beauty(image: Image.Image) -> Image.Image:
    logger.debug("Applying beauty filter")
    rgb = np.array(image.convert("RGB"))
    bgr = cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR)

    smooth = cv2.bilateralFilter(bgr, d=9, sigmaColor=75, sigmaSpace=75)
    detail = cv2.addWeighted(bgr, 0.35, smooth, 0.65, 0)
    result = cv2.cvtColor(detail, cv2.COLOR_BGR2RGB)

    output = Image.fromarray(result)
    output = ImageEnhance.Sharpness(output).enhance(1.1)
    output = ImageEnhance.Contrast(output).enhance(1.03)
    logger.debug("Beauty filter applied")
    return output


def _remove_background(image: Image.Image) -> Image.Image:
    logger.debug("Applying remove_background filter")
    try:
        import mediapipe as mp
    except ImportError:
        logger.warning("MediaPipe not installed, returning original image for remove_background filter")
        return image

    rgb = np.array(image.convert("RGB"))
    segmenter = mp.solutions.selfie_segmentation.SelfieSegmentation(model_selection=1)
    result = segmenter.process(rgb)
    mask = result.segmentation_mask > 0.35

    rgba = np.dstack([rgb, np.where(mask, 255, 0).astype(np.uint8)])
    output = Image.fromarray(rgba, mode="RGBA")
    logger.debug("Remove_background filter applied")
    return output


def apply_filter(image: Image.Image, filter_type: FilterType) -> Image.Image:
    logger.debug(f"apply_filter called with filter_type: {filter_type} (type: {type(filter_type).__name__})")
    
    if filter_type == FilterType.grayscale:
        return _apply_grayscale(image)
    if filter_type == FilterType.blur:
        return _apply_blur(image)
    if filter_type == FilterType.vintage:
        return _apply_vintage(image)
    if filter_type == FilterType.beauty:
        return _apply_beauty(image)
    if filter_type == FilterType.remove_background:
        return _remove_background(image)

    logger.warning(f"Unknown filter type: {filter_type}, returning original image")
    return image


def process_image_bytes(image_bytes: bytes, filter_type: FilterType) -> bytes:
    logger.debug(f"process_image_bytes called with filter_type: {filter_type}")
    
    image = Image.open(BytesIO(image_bytes)).convert("RGB")
    logger.debug(f"Image loaded: {image.size}, mode: {image.mode}")
    
    processed = apply_filter(image, filter_type)
    logger.debug(f"Image processed: {processed.size}, mode: {processed.mode}")
    
    result = _pil_to_bytes(processed)
    logger.debug(f"Image converted to bytes: {len(result)} bytes")
    
    return result