#ifndef BASE_H
#define BASE_H

#ifdef CRIOLLOCORE_EXPORTS
	#ifdef _WIN32
		#define CRIOLLO_API __declspec(dllexport)
	#else // TODO: Other OSes
	#endif
#else
	#ifdef _WIN32
		#define CRIOLLO_API __declspec(dllimport)
	#else // TODO: Other OSes
	#endif
#endif

#ifdef _WIN32
	#ifndef CORECLR_DELEGATE_CALLTYPE
		#define CORECLR_DELEGATE_CALLTYPE __stdcall
	#endif
#else
	#ifndef CORECLR_DELEGATE_CALLTYPE
		#define CORECLR_DELEGATE_CALLTYPE
	#endif
#endif


#endif