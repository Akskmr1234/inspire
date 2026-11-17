"""The WSGI application gunicorn serves, configured from PRIVACYIDEA_CONFIGFILE."""
from privacyidea.app import create_app

application = create_app(config_name="production", config_file="/data/pi.cfg", silent=True)
