"""Standard-library landscape exchange and blind rendering harness."""
__all__ = ['read_package', 'write_package', 'validate_package']


def __getattr__(name):
    if name in __all__:
        from . import package
        return getattr(package,name)
    raise AttributeError(name)
