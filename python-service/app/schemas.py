from enum import Enum


class FilterType(str, Enum):
    grayscale = "grayscale"
    blur = "blur"
    vintage = "vintage"
    beauty = "beauty"
    remove_background = "remove_background"