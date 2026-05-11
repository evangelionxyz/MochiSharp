#pragma once
#ifndef MOCH_VERIFY_HPP
#define MOCH_VERIFY_HPP

#define MOCHI_SOURCE_LOCATION const char* file = __FILE__; int line = __LINE__

#if defined(__GNUC__)
#define MOCHI_DEBUG_BREAK __builtin_trap()
#elif defined(_MSC_VER)
#define MOCHI_DEBUG_BREAK __debugbreak()
#else
#define MOCHI_DEBUG_BREAK	
#endif

#define MOCHI_VERIFY(expr) do {\
						if(!(expr))\
						{\
							MOCHI_SOURCE_LOCATION;\
							std::cerr << "[MochiSharp.Native]: Assert Failed! Expression: " << #expr << " at " << file << ":" << line << "\n";\
							MOCHI_DEBUG_BREAK;\
						}\
					} while(0)


#endif